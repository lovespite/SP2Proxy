
using SP2Proxy.Utils;

namespace SP2Proxy.Core;
public record Frame
{
    public const int MetaSize = 16; // 8 for channelId, 8 for length
    public const int MaxTransmitionUnitSize = 1400; // MTU 
    public const int StackBufferSize = 4096;

    public const byte ByteFlagEsc = 0x10;   // DLE
    public const byte ByteFlagBeg = 0x02;   // STX
    public const byte ByteFlagEnd = 0x03;   // ETX 

    public long ChannelId { get; init; }
    public Memory<byte> Payload { get; set; } = Memory<byte>.Empty;

    public static Frame Empty(long cid) => new() { ChannelId = cid, Payload = Memory<byte>.Empty };

    public unsafe static Frame Build(ReadOnlyMemory<byte> chunk, long cid)
    {
        var buffer = new byte[MetaSize + chunk.Length].AsMemory();

        BitConverter.TryWriteBytes(buffer.Span[..8], cid);
        BitConverter.TryWriteBytes(buffer.Span.Slice(8, 8), (long)chunk.Length);

        fixed (byte* srcPtr = chunk.Span)
        fixed (byte* dstPtr = buffer.Span[MetaSize..])
        {
            Buffer.MemoryCopy(srcPtr, dstPtr, chunk.Length, chunk.Length);
        }

        return new Frame
        {
            ChannelId = cid,
            Payload = buffer,
        };
    }

    public static Frame Parse(ReadOnlySpan<byte> frameBytes)
    {
        var buffer = FrameUtils.Unescape(frameBytes);
        if (buffer.Length < MetaSize)
            throw new InvalidDataException("Frame is smaller than meta size.");

        var cid = BitConverter.ToInt64(buffer.Span[..8]);
        var length = (int)BitConverter.ToInt64(buffer.Span[8..]);

        if (buffer.Length < MetaSize + length)
            throw new InvalidDataException("Frame data is incomplete.");

        return new Frame
        {
            ChannelId = cid,
            Payload = buffer[MetaSize..]
        };
    }
}

// 负责帧的创建、解析和转义
internal static class FrameUtils
{
    public const int StackAllocThreshold = 2048;

    // 将数据块封装成一个完整的待发送数据包（带STX和ETX）
    public static int Pack(Frame frame, Span<byte> buffer)
    {
        var escaped = Escape(frame.Payload.Span, buffer[1..]);

        var packet = buffer[..(escaped + 2)];

        packet[0] = Frame.ByteFlagBeg;
        packet[^1] = Frame.ByteFlagEnd;

        return packet.Length;
    }

    private static int Escape(ReadOnlySpan<byte> source, Span<byte> buffer) => Escape_Optimized(source, buffer);
    public static Memory<byte> Unescape(ReadOnlySpan<byte> escapedBuffer) => Unescape_Optimized(escapedBuffer);

    private static int Escape_Optimized(ReadOnlySpan<byte> source, Span<byte> buffer)
    {
        if (source.IsEmpty) return 0;

        if (source.Length <= StackAllocThreshold)
        {
            return Escape_Stack(source, buffer);
        }
        else
        {
            return Escape_Heap(source, buffer);
        }
    }

    // 将转义结果写入指定的Span
    private unsafe static int Escape_Stack(ReadOnlySpan<byte> source, Span<byte> buffer)
    {
        var maxSize = source.Length * 2;
        if (buffer.Length < maxSize)
            throw new ArgumentException("Buffer is too small for the escaped data.");

        Span<byte> destination = buffer[..maxSize];
        int destIndex = 0;

        fixed (byte* srcPtr = source)
        fixed (byte* dstPtr = destination)
        {
            byte* src = srcPtr;
            byte* dst = dstPtr;
            byte* srcEnd = src + source.Length;
            while (src < srcEnd)
            {
                byte b = *src++;
                if (b == Frame.ByteFlagEsc || b == Frame.ByteFlagBeg || b == Frame.ByteFlagEnd)
                {
                    *dst++ = Frame.ByteFlagEsc;
                    *dst++ = (byte)(b ^ 0xFF);
                    destIndex += 2;
                }
                else
                {
                    *dst++ = b;
                    destIndex++;
                }
            }
        }

        return destIndex;
    }

    private unsafe static int Escape_Heap(ReadOnlySpan<byte> source, Span<byte> buffer)
    {
        if (source.IsEmpty) return 0;

        // 第一遍扫描：计算需要转义的字节数
        int escapeCount = 0;
        fixed (byte* bufferPtr = source)
        {
            byte* ptr = bufferPtr;
            byte* end = ptr + source.Length;

            while (ptr < end)
            {
                if (*ptr == Frame.ByteFlagEsc || *ptr == Frame.ByteFlagBeg || *ptr == Frame.ByteFlagEnd)
                {
                    escapeCount++;
                }
                ptr++;
            }
        }

        var totalSize = source.Length + escapeCount;
        if (buffer.Length < totalSize)
            throw new ArgumentException("Buffer is too small for the escaped data.");

        // 分配精确大小的结果数组
        var result = buffer[..totalSize];

        fixed (byte* bufferPtr = source)
        fixed (byte* resultPtr = result)
        {
            byte* src = bufferPtr;
            byte* dst = resultPtr;
            byte* srcEnd = src + source.Length;

            while (src < srcEnd)
            {
                byte b = *src++;
                if (b == Frame.ByteFlagEsc || b == Frame.ByteFlagBeg || b == Frame.ByteFlagEnd)
                {
                    *dst++ = Frame.ByteFlagEsc;
                    *dst++ = (byte)(b ^ 0xFF);
                }
                else
                {
                    *dst++ = b;
                }
            }
        }

        return totalSize;
    }

    // 对接收到的数据进行反转义 - unsafe优化版本
    public static unsafe Memory<byte> Unescape_Optimized(ReadOnlySpan<byte> escapedBuffer)
    {
        if (escapedBuffer.IsEmpty) return Memory<byte>.Empty;

        // 预分配最大可能的大小，稍后可能会缩小
        Memory<byte> result = new byte[escapedBuffer.Length];
        int resultLength = 0;

        fixed (byte* escapedPtr = escapedBuffer)
        fixed (byte* resultPtr = result.Span)
        {
            byte* src = escapedPtr;
            byte* dst = resultPtr;
            byte* srcEnd = src + escapedBuffer.Length;

            while (src < srcEnd)
            {
                byte b = *src++;
                if (b == Frame.ByteFlagEsc && src < srcEnd)
                {
                    *dst++ = (byte)(*src++ ^ 0xFF);
                }
                else
                {
                    *dst++ = b;
                }
                resultLength++;
            }
        }

        return result[..resultLength];
    }
}
