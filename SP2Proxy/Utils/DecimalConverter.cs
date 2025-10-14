namespace SP2Proxy.Utils;

/// <summary>
/// 提供用于在 decimal 和 byte[] 之间进行转换的静态方法。
/// </summary>
public static class DecimalConverter
{
    private const int DecimalSizeInBytes = 16;

    extension(decimal dec)
    {
        /// <summary>
        /// 将一个 decimal 值转换为 16 字节的数组。
        /// </summary>
        /// <param name="dec">要转换的 decimal 值。</param>
        /// <returns>一个包含 16 个字节的数组。</returns>
        public byte[] ToBytes()
        {
            // 1. 使用 decimal.GetBits 获取 decimal 的内部整数表示（4个int）。
            int[] bits = decimal.GetBits(dec);

            // 2. 创建一个 16 字节的数组来存储结果。
            byte[] bytes = new byte[DecimalSizeInBytes];

            // 3. 将每个 int 转换为字节并复制到目标数组中。
            //    为了避免手动管理索引的复杂性，这里使用 Buffer.BlockCopy，它性能很高。

            // 复制第一个 int (bits[0]) 到 bytes[0-3]
            Buffer.BlockCopy(BitConverter.GetBytes(bits[0]), 0, bytes, 0, 4);

            // 复制第二个 int (bits[1]) 到 bytes[4-7]
            Buffer.BlockCopy(BitConverter.GetBytes(bits[1]), 0, bytes, 4, 4);

            // 复制第三个 int (bits[2]) 到 bytes[8-11]
            Buffer.BlockCopy(BitConverter.GetBytes(bits[2]), 0, bytes, 8, 4);

            // 复制第四个 int (bits[3]) 到 bytes[12-15]
            Buffer.BlockCopy(BitConverter.GetBytes(bits[3]), 0, bytes, 12, 4);

            return bytes;
        }

        /// <summary>
        /// 将 decimal 值写入到提供的 Span<byte> 中。
        /// </summary>
        /// <param name="buffer"></param>
        /// <exception cref="ArgumentException"></exception>
        public void WriteToBytes(Span<byte> buffer)
        {
            if (buffer.Length < DecimalSizeInBytes)
            {
                throw new ArgumentException($"目标缓冲区长度必须至少为 {DecimalSizeInBytes} 字节。", nameof(buffer));
            }

            int[] bits = decimal.GetBits(dec);

            // 直接使用 Span<byte> 来写入字节数据，避免额外的数组分配
            BitConverter.TryWriteBytes(buffer[..4], bits[0]);
            BitConverter.TryWriteBytes(buffer.Slice(4, 4), bits[1]);
            BitConverter.TryWriteBytes(buffer.Slice(8, 4), bits[2]);
            BitConverter.TryWriteBytes(buffer.Slice(12, 4), bits[3]);
        }
    }

    extension(byte[] bytes)
    {
        /// <summary>
        /// 将一个 16 字节的数组转换为 decimal 值。
        /// </summary>
        /// <param name="bytes">要转换的 16 字节数组。</param>
        /// <returns>转换后的 decimal 值。</returns>
        /// <exception cref="ArgumentNullException">如果输入的数组为 null。</exception>
        /// <exception cref="ArgumentException">如果输入的数组长度不为 16。</exception>
        public decimal ToDecimal()
        {
            // 1. 输入验证，确保数组有效。
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes), "输入的字节数组不能为 null。");
            }

            if (bytes.Length != DecimalSizeInBytes)
            {
                throw new ArgumentException($"输入的字节数组长度必须是 {DecimalSizeInBytes} 字节。", nameof(bytes));
            }

            // 2. 创建一个 int[4] 数组来接收转换后的整数。
            int[] bits = new int[4];

            // 3. 从字节数组中提取4个整数。
            //    使用 BitConverter.ToInt32 并指定起始索引。
            bits[0] = BitConverter.ToInt32(bytes, 0);
            bits[1] = BitConverter.ToInt32(bytes, 4);
            bits[2] = BitConverter.ToInt32(bytes, 8);
            bits[3] = BitConverter.ToInt32(bytes, 12);

            // 4. 使用包含内部表示的 int 数组来构造 decimal。
            return new decimal(bits);
        }
    }
}