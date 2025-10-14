using SP2Proxy.Utils;
using System.Buffers;
using System.IO.Pipelines;

namespace SP2Proxy.Core;

public class FramePipeline
{
    private readonly Pipe _pipe = new();

    // 外部调用者（例如，网络监听器）将接收到的数据写入这里
    public PipeWriter Writer => _pipe.Writer;

    // 解析器的主逻辑，应该在一个独立的Task中运行
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ParseFramesAsync()
    {
        while (true)
        {
            // 等待管道中有数据可读
            ReadResult result = await _pipe.Reader.ReadAsync();
            ReadOnlySequence<byte> buffer = result.Buffer;

            // 持续解析，直到缓冲区中没有完整的帧
            while (TryReadFrame(ref buffer, out ReadOnlyMemory<byte> frame))
            {
                yield return frame;
            }

            // 告诉管道我们已经检查了多少数据，以及消费了多少
            // buffer 的起始位置可能已经因为 TryReadFrame 的调用而改变
            _pipe.Reader.AdvanceTo(buffer.Start, buffer.End);

            // 如果读取被取消或管道已完成，则退出
            if (result.IsCanceled || result.IsCompleted)
            {
                break;
            }
        }
    }

    private static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out ReadOnlyMemory<byte> frame)
    {
        frame = default;

        // 使用 SequenceReader 来高效地操作 ReadOnlySequence<byte>
        var reader = new SequenceReader<byte>(buffer);

        // 1. 查找起始符
        if (!reader.TryReadTo(out ReadOnlySpan<byte> _, Frame.ByteFlagBeg, advancePastDelimiter: true))
        {
            // 没有找到起始符，这部分数据可以丢弃
            // 我们通过将整个缓冲区标记为已检查来“丢弃”它
            buffer = buffer.Slice(buffer.End);
            return false;
        }

        // 2. 查找结束符
        if (!reader.TryReadTo(out ReadOnlySequence<byte> frameSequence, Frame.ByteFlagEnd, advancePastDelimiter: true))
        {
            // 找到了起始符，但没有找到结束符，需要更多数据
            // 将 buffer 的起始位置移到起始符的位置，等待下一次数据到来
            // 注意：我们已经在上面 advancePastDelimiter: true 了，所以当前 reader.Position 就是起始符之后
            buffer = buffer.Slice(reader.Position);
            // 由于上面已经越过了起始符，所以需要回退一个位置
            buffer = buffer.Slice(buffer.GetPosition(-1, buffer.Start));
            return false;
        }

        // 3. 成功找到一个完整的帧
        // frameSequence 现在包含了起始符和结束符之间的数据
        // 注意：如果帧数据可能跨越多个内存块，ToArray() 会将其合并。
        // 如果能直接处理 ReadOnlySequence<byte>，可以避免这次分配。
        frame = frameSequence.ToArray();

        // 4. 更新 buffer，使其指向已处理数据的末尾
        buffer = buffer.Slice(reader.Position);

        return true;
    }
}