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
            SequencePosition consumed = buffer.Start;
            SequencePosition examined = buffer.End;

            // 持续解析，直到缓冲区中没有完整的帧
            while (TryReadFrame(ref buffer, out ReadOnlyMemory<byte> frame, out SequencePosition frameEnd))
            {
                yield return frame;
                consumed = frameEnd;
            }

            // 告诉管道我们已经检查了多少数据，以及消费了多少
            _pipe.Reader.AdvanceTo(consumed, examined);

            // 如果读取被取消或管道已完成，则退出
            if (result.IsCanceled || result.IsCompleted)
            {
                break;
            }
        }
    }

    private static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out ReadOnlyMemory<byte> frame, out SequencePosition frameEnd)
    {
        frame = default;
        frameEnd = buffer.Start;

        // 使用 SequenceReader 来高效地操作 ReadOnlySequence<byte>
        var reader = new SequenceReader<byte>(buffer);

        // 1. 查找起始符
        if (!reader.TryReadTo(out ReadOnlySpan<byte> _, Frame.ByteFlagBeg, advancePastDelimiter: false))
        {
            // 没有找到起始符，整个缓冲区都可以丢弃
            buffer = ReadOnlySequence<byte>.Empty;
            return false;
        }

        // 记住起始符的位置
        var startPosition = reader.Position;

        // 跳过起始符
        reader.Advance(1);

        // 2. 查找结束符
        if (!reader.TryReadTo(out ReadOnlySequence<byte> frameSequence, Frame.ByteFlagEnd, advancePastDelimiter: true))
        {
            // 找到了起始符，但没有找到结束符，需要更多数据
            // 保留从起始符开始的所有数据
            buffer = buffer.Slice(startPosition);
            return false;
        }

        // 3. 成功找到一个完整的帧
        // frameSequence 现在包含了起始符和结束符之间的数据（不包括标志符本身）
        // 这与 FrameParser 的行为保持一致
        frame = frameSequence.ToArray();
        frameEnd = reader.Position;

        // 4. 更新 buffer，使其指向已处理数据的末尾
        buffer = buffer.Slice(frameEnd);

        return true;
    }
}