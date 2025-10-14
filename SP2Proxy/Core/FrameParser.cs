namespace SP2Proxy.Core;

// 从字节流中解析出完整的数据帧
public class FrameParser
{
    private byte[] _buffer = [];

    public unsafe IReadOnlyList<byte[]> Parse(ReadOnlySpan<byte> newData)
    {
        var results = new List<byte[]>();

        // 将新数据附加到内部缓冲区
        var newBuffer = new byte[_buffer.Length + newData.Length];

        fixed (byte* bufferPtr = _buffer)
        fixed (byte* newDataPtr = newData)
        fixed (byte* newBufferPtr = newBuffer)
        {
            // 使用指针快速复制数据
            Buffer.MemoryCopy(bufferPtr, newBufferPtr, newBuffer.Length, _buffer.Length);
            Buffer.MemoryCopy(newDataPtr, newBufferPtr + _buffer.Length, newBuffer.Length - _buffer.Length, newData.Length);
        }

        _buffer = newBuffer;

        while (true)
        {
            int frameStartIndex = -1;
            int frameEndIndex = -1;
            int bufferLength = _buffer.Length;

            fixed (byte* bufferPtr = _buffer)
            {
                // 快速搜索帧起始符 (STX)
                for (int i = 0; i < bufferLength; i++)
                {
                    if (*(bufferPtr + i) == Frame.ByteFlagBeg)
                    {
                        frameStartIndex = i;
                        break;
                    }
                }

                if (frameStartIndex == -1)
                {
                    _buffer = []; // 没有起始符，清空缓冲区
                    break;
                }

                // 从起始符后开始查找结束符 (ETX)
                for (int i = frameStartIndex + 1; i < bufferLength; i++)
                {
                    if (*(bufferPtr + i) == Frame.ByteFlagEnd)
                    {
                        frameEndIndex = i;
                        break;
                    }
                }

                if (frameEndIndex == -1)
                {
                    // 没有结束符，保留从起始符开始的数据等待下次
                    int remainingLength = bufferLength - frameStartIndex;
                    var remainingBuffer = new byte[remainingLength];
                    fixed (byte* remainingPtr = remainingBuffer)
                    {
                        Buffer.MemoryCopy(bufferPtr + frameStartIndex, remainingPtr, remainingLength, remainingLength);
                    }
                    _buffer = remainingBuffer;
                    break;
                }

                // 提取帧数据 (不包括 STX 和 ETX)
                int frameDataLength = frameEndIndex - frameStartIndex - 1;
                if (frameDataLength > 0)
                {
                    var frameData = new byte[frameDataLength];
                    fixed (byte* frameDataPtr = frameData)
                    {
                        Buffer.MemoryCopy(bufferPtr + frameStartIndex + 1, frameDataPtr, frameDataLength, frameDataLength);
                    }
                    results.Add(frameData);
                }

                // 移除已处理的数据
                int remainingDataLength = bufferLength - frameEndIndex - 1;
                if (remainingDataLength > 0)
                {
                    var newRemainingBuffer = new byte[remainingDataLength];
                    fixed (byte* newRemainingPtr = newRemainingBuffer)
                    {
                        Buffer.MemoryCopy(bufferPtr + frameEndIndex + 1, newRemainingPtr, remainingDataLength, remainingDataLength);
                    }
                    _buffer = newRemainingBuffer;
                }
                else
                {
                    _buffer = [];
                }
            }
        }

        return results;
    }
}
