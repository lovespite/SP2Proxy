using System.Buffers;
using System.IO.Pipelines;

namespace SP2Proxy.Core;
// 代表一个虚拟信道，继承自 .NET 的 Stream
public class Channel : DuplexStream
{
    protected readonly SerialPort2 _host;

    private readonly Pipe _incomingData = new();
    private readonly Action<Channel, int> _onClose;

    public long Cid { get; }
    public string Path => _host.Path;

    public Channel(long id, SerialPort2 host, Action<Channel, int> onClose)
    {
        Cid = id;
        _host = host;
        _onClose = onClose;
    }

    // 优先实现这个 Memory<byte> 的重载
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var result = await _incomingData.Reader.ReadAsync(cancellationToken);
        var readableBuffer = result.Buffer;

        // 计算实际可以拷贝的长度
        var len = (int)Math.Min(readableBuffer.Length, buffer.Length);
        if (len == 0)
        {
            // 如果缓冲区为空且读取已完成，返回0表示流结束
            return result.IsCompleted ? 0 : len;
        }

        // 从管道的缓冲区拷贝到目标 buffer
        var slice = readableBuffer.Slice(0, len);
        slice.CopyTo(buffer.Span);

        // 告知管道我们已经处理了多少数据
        _incomingData.Reader.AdvanceTo(slice.End);

        return len;
    }

    // 旧的重载可以调用新的重载来保持兼容性
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        // 调用更现代的 Memory<T> 重载
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    // 将数据分片、打包成帧并发送 
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var dataToWrite = buffer.AsMemory(offset, count);
        var currentOffset = 0;

        while (currentOffset < dataToWrite.Length)
        {
            var chunkSize = Math.Min(Frame.MaxTransmitionUnitSize, dataToWrite.Length - currentOffset);
            var chunk = dataToWrite.Slice(currentOffset, chunkSize);

            var frame = Frame.Build(chunk, Cid);

            // _host.EnqueueOut(frame);
            await _host.EnqueueOutAsync(frame, cancellationToken);
            currentOffset += chunkSize;
        }

        await Task.CompletedTask;
    }

    // 外部调用的方法，用于将接收到的数据写入信道
    public async Task PushExternalDataAsync(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
        {
            await _incomingData.Writer.CompleteAsync();
            return;
        }
        await _incomingData.Writer.WriteAsync(data);
    }

    public override void Close()
    {
        // 发送一个空帧来通知对方信道已关闭 
        _host.EnqueueOut(Frame.Empty(Cid));
        _onClose(this, 0); // 触发关闭回调
        _incomingData.Writer.Complete();
        _incomingData.Reader.Complete();
        base.Close();
    }

    // 其他必须重写的方法
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

public abstract class DuplexStream : Stream
{
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count).GetAwaiter().GetResult();
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
}


public interface IChannelFactory
{
    Channel NewChannel(long? id = null);
}