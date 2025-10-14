using SP2Proxy.Core;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace SP2Proxy.Utils;

public partial class Map : IStringSerializable, IEnumerable<KeyValuePair<string, object>>, ICloneable
{
    public static Map New() => [];
    public static Map NewCaseInsensitive() => new(caseSensitive: false);
    public static Map NewSynchronized() => new(concurrent: true);
    public static Map Deserialize(string base64) => SerializerHelper.FromBase64(base64);
    public static Map Deserialize(ReadOnlySpan<byte> data) => SerializerHelper.FromBinaryData(data);
    public static Map Deserialize(Stream stream) => SerializerHelper.FromStream(stream);

    public const int MAX_KEY_LENGTH = 128;
    public const int MAX_VAL_LENGTH = 4096;

    [Flags]
    public enum MapFlags : byte // 指定底层类型为 byte
    {
        None = 0,
        CaseInsensitive = 1 << 0, // 0b0000_0001 
        ReadOnly = 1 << 1,        // 0b0000_0010 
        Concurrent = 1 << 2,      // 0b0000_0100
    }

    public enum MapValueType : byte
    {
        Unspecified = 0,
        String = 1,
        Boolean = 2,
        ByteArray = 3,
        Byte = 4,
        Int16 = 5,
        UInt16 = 6,
        Int32 = 7,
        UInt32 = 8,
        Int64 = 9,
        UInt64 = 10,
        Float = 11,
        Double = 12,
        Decimal = 13,
        Map = 14,
        Guid = 15,
    }

    public bool CaseSensitive
    {
        // 3. 使用 HasFlag() 方法，可读性更高
        get => !_flags.HasFlag(MapFlags.CaseInsensitive);
        private set
        {
            if (value) // CaseSensitive = true
            {
                _flags &= ~MapFlags.CaseInsensitive; // 移除 CaseInsensitive 标志
            }
            else // CaseSensitive = false
            {
                _flags |= MapFlags.CaseInsensitive; // 添加 CaseInsensitive 标志
            }
        }
    }

    public bool ReadOnly
    {
        get => _flags.HasFlag(MapFlags.ReadOnly);
        set
        {
            if (value)
            {
                _flags |= MapFlags.ReadOnly; // 添加 ReadOnly 标志
            }
            else
            {
                _flags &= ~MapFlags.ReadOnly; // 移除 ReadOnly 标志
            }
        }
    }

    public bool Concurrent
    {
        get => _flags.HasFlag(MapFlags.Concurrent);
        private set
        {
            if (value)
            {
                _flags |= MapFlags.Concurrent; // 添加 Concurrent 标志
            }
            else
            {
                _flags &= ~MapFlags.Concurrent; // 移除 Concurrent 标志
            }
        }
    }


    protected readonly IDictionary<string, object> _dictionary;
    public Map(bool caseSensitive = true, bool concurrent = false)
    {
        CaseSensitive = caseSensitive;
        Concurrent = concurrent;

        if (CaseSensitive)
        {
            _dictionary = concurrent
                ? new ConcurrentDictionary<string, object>()
                : new Dictionary<string, object>();
        }
        else
        {
            _dictionary = concurrent
               ? new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase)
               : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public Map(MapFlags flags) : this(
        caseSensitive: !flags.HasFlag(MapFlags.CaseInsensitive),
        concurrent: flags.HasFlag(MapFlags.Concurrent))
    {
        _flags = flags;
    }

    public int Size => _dictionary.Count;

    private MapFlags _flags = MapFlags.None;
    public IEnumerable<string> Keys => _dictionary.Keys;
    public IEnumerable<object> Values => _dictionary.Values;
    public IEnumerable<KeyValuePair<string, object>> Entries => _dictionary;

    public Map Lock()
    {
        ReadOnly = true;
        return this;
    }

    public ReadOnlySpan<byte> ToBinaryData()
    {
        using var ms = new MemoryStream();
        ToBinaryData(ms);
        return ms.GetBuffer().AsSpan(0, (int)ms.Length);
    }

    public void ToBinaryData(Stream ms)
    {
        // Detect loop references

        SerializerHelper.CheckForLoop(this);

        // Header
        ms.WriteByte(0xFE);
        ms.WriteByte(0xEF);

        ms.WriteByte(0x01); // Version
        ms.WriteByte((byte)_flags); // Flags

        var buffer = new byte[MAX_VAL_LENGTH * 2].AsSpan();

        foreach (var entry in _dictionary)
            SerializerHelper.WriteEntry(ms, entry, buffer);

        // Footer
        ms.WriteByte(0xEF);
        ms.WriteByte(0xFE);
    }
    public string SerializeAsBase64()
    {
        return Convert.ToBase64String(ToBinaryData());
    }

    public void SerializeTo(Stream stream)
    {
        ToBinaryData(stream);
    }

    private void ThrowIfReadOnly()
    {
        if (ReadOnly)
            throw new InvalidOperationException("Map is read-only");
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        if (key.Contains('\0'))
            throw new ArgumentException("Key cannot contain null character", nameof(key));

        if (Encoding.UTF8.GetByteCount(key) > MAX_KEY_LENGTH)
            throw new ArgumentOutOfRangeException(nameof(key),
                $"Key length exceeds maximum allowed length of {MAX_KEY_LENGTH}");
    }

    private static void ValidateValue(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value is not (Guid or Map or string or bool or byte[] or byte or short or ushort or int or uint or long or ulong or float or double or decimal))
            throw new ArgumentException("Unsupported value type: " + value.GetType().FullName, nameof(value));

        if (value is string str && Encoding.UTF8.GetByteCount(str) > MAX_VAL_LENGTH)
            throw new ArgumentOutOfRangeException(nameof(value), "String value length exceeds maximum allowed length of " + MAX_VAL_LENGTH);

        if (value is byte[] bytes && bytes.Length > MAX_VAL_LENGTH)
            throw new ArgumentOutOfRangeException(nameof(value), "Byte array value length exceeds maximum allowed length of " + MAX_VAL_LENGTH);
    }


    public void Clear()
    {
        ThrowIfReadOnly();
        _dictionary.Clear();
    }

    public bool Has(string key)
    {
        return _dictionary.ContainsKey(key);
    }

    public void Add(string key, object value)
    {
        SetInternal(key, value);
    }

    protected void SetInternal(string key, object value)
    {
        ThrowIfReadOnly();

        ValidateKey(key);
        ValidateValue(value);

        _dictionary[key] = value;
    }

    public Map Set(object value, [CallerMemberName] string? key = null)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key), "Anonymous call is not supported.");

        SetInternal(key, value);
        return this;
    }

    public Map Set(string key, Guid value, bool storeAsString = false)
    {
        if (storeAsString)
        {
            SetInternal(key, value.ToString());
            return this;
        }

        SetInternal(key, value);
        return this;
    }

    public Map Set(string key, object value)
    {
        SetInternal(key, value);
        return this;
    }

    public Map Set(string key, string value)
    {
        SetInternal(key, value);
        return this;
    }

    public Map Set(string key, bool value)
    {
        SetInternal(key, value);
        return this;
    }

    public Map Set(string key, byte[] value)
    {
        SetInternal(key, value);
        return this;
    }

    public Map Set<T>(string key, T value) where T : INumber<T>
    {
        SetInternal(key, value);
        return this;
    }

    public Map Set(string key, Map map)
    {
        if (ReferenceEquals(this, map))
            throw new ArgumentException("Cannot set a Map to itself", nameof(map));

        if (SerializerHelper.ContainsReference(map, this))
            throw new ArgumentException("Cannot set a Map that would create a reference loop", nameof(map));

        SetInternal(key, map);
        return this;
    }

    public object Get([CallerMemberName] string? key = null)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key), "Anonymous call is not supported.");

        if (_dictionary.TryGetValue(key, out var value) && value is not null)
        {
            return value;
        }

        throw new KeyNotFoundException($"Key '{key}' not found in Map.");
    }

    public Optional<T> Get<T>(string key) where T : notnull
    {
        try
        {
            if (_dictionary.TryGetValue(key, out var value) && value is not null)
                return ConvertValue<T>(value);
        }
        catch
        {
        }

        return Optional<T>.Empty();
    }

    public void Delete(string key)
    {
        ThrowIfReadOnly();

        if (Concurrent && _dictionary is ConcurrentDictionary<string, object> concurrentDict)
        {
            concurrentDict.TryRemove(key, out _);
        }
        else
        {
            _dictionary.Remove(key);
        }
    }

    public bool TryGet<T>(string key, out T? value) where T : notnull
    {
        if (_dictionary.TryGetValue(key, out var val) && val is not null)
        {
            try
            {
                value = ConvertValue<T>(val);
                return true;
            }
            catch
            {
            }
        }

        value = default;

        return false;
    }

    public bool TryDelete<T>(string key, out T? value) where T : notnull
    {
        ThrowIfReadOnly();

        if (Concurrent && _dictionary is ConcurrentDictionary<string, object> concurrentDict)
        {
            if (concurrentDict.TryRemove(key, out var val) && val is not null)
            {
                value = ConvertValue<T>(val);
                return true;
            }
        }
        else
        {
            // 使用锁保护非并发字典的操作
            lock (_dictionary)
            {
                if (_dictionary.TryGetValue(key, out var val) && val is not null)
                {
                    _dictionary.Remove(key);
                    value = ConvertValue<T>(val);
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static T ConvertValue<T>(object value) where T : notnull
    {
        return value switch
        {
            T directValue => directValue,
            IConvertible convertible => (T)convertible.ToType(typeof(T), null),
            _ => (T)Convert.ChangeType(value, typeof(T))
        };
    }

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
    {
        return _dictionary.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)_dictionary).GetEnumerator();
    }


    public Optional<T> GetPath<T>(string path, char separator) where T : notnull
    {
        if (string.IsNullOrEmpty(path))
            return Optional<T>.Empty();

        var parts = path.Split(separator);
        Map? current = this;

        // 遍历路径的所有部分，除了最后一个
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (string.IsNullOrEmpty(part))
                return Optional<T>.Empty();

            if (!current._dictionary.TryGetValue(part, out var value) || value is not Map nestedMap)
                return Optional<T>.Empty();

            current = nestedMap;
        }

        // 获取最后一个键的值
        var lastKey = parts[^1];
        return current.Get<T>(lastKey);
    }
    public Optional<T> GetPath<T>(string path) where T : notnull => GetPath<T>(path, '.');


    public bool TryGetPath<T>(string path, char separator, out T? value) where T : notnull
    {
        value = default;

        if (string.IsNullOrEmpty(path))
            return false;

        var parts = path.Split(separator);
        Map? current = this;

        // 遍历路径的所有部分，除了最后一个
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (string.IsNullOrEmpty(part))
                return false;

            if (!current._dictionary.TryGetValue(part, out var val) || val is not Map nestedMap)
                return false;

            current = nestedMap;
        }

        // 获取最后一个键的值
        var lastKey = parts[^1];
        return current.TryGet(lastKey, out value);
    }
    public bool TryGetPath<T>(string path, out T? value) where T : notnull => TryGetPath(path, '.', out value);


    public Map SetPath(string path, char separator, object value)
    {
        ThrowIfReadOnly();

        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path cannot be null or empty", nameof(path));

        var parts = path.Split(separator);
        Map current = this;

        // 创建或遍历到倒数第二级的Map
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (string.IsNullOrEmpty(part))
                throw new ArgumentException($"Invalid path segment at position {i}", nameof(path));

            if (current._dictionary.TryGetValue(part, out var existingValue))
            {
                if (existingValue is not Map nestedMap)
                    throw new InvalidOperationException($"Path segment '{part}' exists but is not a Map");
                current = nestedMap;
            }
            else
            {
                // 创建新的嵌套Map，使用与当前Map相同的设置
                var newMap = new Map(current.CaseSensitive, current.Concurrent);
                current.SetInternal(part, newMap);
                current = newMap;
            }
        }

        // 设置最终值
        var lastKey = parts[^1];
        current.SetInternal(lastKey, value);
        return this;
    }
    public Map SetPath(string path, object value) => SetPath(path, '.', value);

    public bool HasPath(string path, char separator)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var parts = path.Split(separator);
        Map? current = this;

        // 遍历路径的所有部分，除了最后一个
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (string.IsNullOrEmpty(part))
                return false;

            if (!current._dictionary.TryGetValue(part, out var value) || value is not Map nestedMap)
                return false;

            current = nestedMap;
        }

        // 检查最后一个键是否存在
        var lastKey = parts[^1];
        return current.Has(lastKey);
    }
    public bool HasPath(string path) => HasPath(path, '.');

    public void DeletePath(string path, char separator)
    {
        ThrowIfReadOnly();

        if (string.IsNullOrEmpty(path))
            return;

        var parts = path.Split(separator);
        Map? current = this;

        // 遍历路径的所有部分，除了最后一个
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (string.IsNullOrEmpty(part))
                return;

            if (!current._dictionary.TryGetValue(part, out var value) || value is not Map nestedMap)
                return; // 路径不存在，无需删除

            current = nestedMap;
        }

        // 删除最后一个键
        var lastKey = parts[^1];
        current.Delete(lastKey);
    }
    public void DeletePath(string path) => DeletePath(path, '.');
}

partial class Map
{
    public static class SerializerHelper
    {
        private static readonly ArrayPool<byte> BufferPool = ArrayPool<byte>.Create(MAX_VAL_LENGTH * 2, 50);
        public static Map FromStream(Stream stream)
        {
            // 读取 Header
            var header1 = stream.ReadByte();
            var header2 = stream.ReadByte();
            if (header1 != 0xFE || header2 != 0xEF)
                throw new InvalidDataException("Invalid file header");

            var version = stream.ReadByte();
            if (version != 0x01)
                throw new NotSupportedException($"Unsupported version: {version}");

            var flags = (MapFlags)stream.ReadByte();
            var map = new Map(flags);

            var buffer = BufferPool.Rent(MAX_VAL_LENGTH * 2);

            try
            {
                // 读取条目直到遇到 Footer
                while (true)
                {
                    var peek = stream.ReadByte();
                    if (peek == -1) break;

                    // 检查是否为 Footer
                    if (peek == 0xEF)
                    {
                        var footer2 = stream.ReadByte();
                        if (footer2 == 0xFE) break; // 正常结束
                        throw new InvalidDataException("Invalid file footer");
                    }

                    // 回退一个字节，开始读取键值对
                    stream.Position--;
                    ReadEntry(stream, map, buffer);
                }

                return map;
            }
            finally
            {
                BufferPool.Return(buffer);
            }
        }

        public static Map FromBinaryData(ReadOnlySpan<byte> data)
        {
            using var ms = new MemoryStream(data.ToArray());
            return FromStream(ms);
        }

        public static Map FromBase64(string base64)
        {
            var data = Convert.FromBase64String(base64);
            return FromBinaryData(data);
        }


        /// <summary>
        /// 检查 'container' 及其所有子孙节点是否包含对 'target' 的引用。
        /// 使用一个 seen set 来防止在已存在循环的图上无限递归。
        /// </summary>
        /// <param name="container">要搜索的Map容器。</param>
        /// <param name="target">要查找的目标Map实例。</param>
        /// <returns>如果找到返回true，否则返回false。</returns>
        public static bool ContainsReference(Map container, Map target, HashSet<Map>? seen = null)
        {
            if (container == null || target == null)
                return false;

            // 为顶级调用初始化seen集合
            seen ??= [];

            // 用Add的返回值来检查是否已经访问过，防止无限循环
            if (!seen.Add(container))
                return false; // 已经处理过这个节点了，直接返回

            foreach (var value in container.Values)
            {
                if (value is Map nestedMap)
                {
                    // 找到了直接引用
                    if (ReferenceEquals(nestedMap, target))
                        return true;

                    // 递归搜索子节点
                    if (ContainsReference(nestedMap, target, seen))
                        return true;
                }
            }

            // 注意：这里不需要像LoopReferenceCheck那样Remove，因为我们只是想知道“是否包含”，
            // 而不是跟踪特定的“路径”。seen集合在这里的作用是防止重复工作和死循环。
            return false;
        }

        /// <summary>
        /// Public entry point to check for reference loops in a Map.
        /// 公共入口方法，用于检查Map中的循环引用。
        /// </summary>
        /// <param name="map">The map to check.</param>
        public static void CheckForLoop(Map map)
        {
            // 创建 HashSet 并调用私有的递归辅助方法
            LoopReferenceCheck(map, []);
        }

        /// <summary>
        /// The private recursive helper method.
        /// 私有的递归辅助方法。
        /// </summary>
        private static void LoopReferenceCheck(Map map, HashSet<Map> seenMaps)
        {
            if (map == null) return; // 最好加一个null检查

            if (!seenMaps.Add(map))
            {
                // 如果添加失败，说明已经见过这个Map，存在循环引用
                throw new ArgumentException("Map contains a reference loop", nameof(map));
            }

            foreach (var value in map.Values)
            {
                if (value is Map nestedMap)
                {
                    LoopReferenceCheck(nestedMap, seenMaps);
                }
            }

            // 回溯时移除
            seenMaps.Remove(map);
        }

        public static void WriteEntry(Stream ms, KeyValuePair<string, object> entry, Span<byte> buffer)
        {
            WriteKeyData(ms, entry.Key, buffer);
            WriteValueData(ms, entry.Value, buffer);
        }

        private static void ReadEntry(Stream stream, Map map, Span<byte> buffer)
        {
            int read;
            // 读取键长度
            read = stream.Read(buffer[..2]);
            if (read != 2)
                throw new EndOfStreamException("Unexpected end of stream while reading key length");
            var keyLength = BitConverter.ToUInt16(buffer[..2]);
            if (keyLength <= 0 || keyLength > MAX_KEY_LENGTH)
                throw new InvalidDataException($"Invalid key length: {keyLength}");

            // 读取键
            read = stream.Read(buffer[..keyLength]);
            if (read != keyLength)
                throw new EndOfStreamException("Unexpected end of stream while reading key");

            var key = Encoding.UTF8.GetString(buffer[..keyLength]);

            // 读取值类型
            var valueType = (MapValueType)stream.ReadByte();

            // 根据类型读取值
            object value = valueType switch
            {
                MapValueType.String => ReadStringValue(stream, buffer),
                MapValueType.Boolean => stream.ReadByte() == 1,
                MapValueType.ByteArray => ReadByteArrayValue(stream, buffer),
                MapValueType.Byte => (byte)stream.ReadByte(),
                MapValueType.Int16 => ReadInt16Value(stream, buffer),
                MapValueType.UInt16 => ReadUInt16Value(stream, buffer),
                MapValueType.Int32 => ReadInt32Value(stream, buffer),
                MapValueType.UInt32 => ReadUInt32Value(stream, buffer),
                MapValueType.Int64 => ReadInt64Value(stream, buffer),
                MapValueType.UInt64 => ReadUInt64Value(stream, buffer),
                MapValueType.Float => ReadFloatValue(stream, buffer),
                MapValueType.Double => ReadDoubleValue(stream, buffer),
                MapValueType.Decimal => ReadDecimalValue(stream, buffer),
                MapValueType.Map => FromStream(stream),
                MapValueType.Guid => ReadGuidValue(stream, buffer),
                _ => throw new NotSupportedException($"Unsupported value type: {valueType}")
            };

            map._dictionary[key] = value;
        }

        private static string ReadStringValue(Stream stream, Span<byte> buffer)
        {
            stream.ReadExactly(buffer[..2]);
            var arrLength = BitConverter.ToUInt16(buffer[..2]);

            if (arrLength < 0 || arrLength > MAX_VAL_LENGTH)
                throw new InvalidDataException($"Byte array length exceeds maximum allowed length: {arrLength}");

            stream.ReadExactly(buffer[..arrLength]);

            return Encoding.UTF8.GetString(buffer[..arrLength]);
        }

        private static byte[] ReadByteArrayValue(Stream stream, Span<byte> buffer)
        {
            stream.ReadExactly(buffer[..2]);
            var arrLength = BitConverter.ToUInt16(buffer[..2]);

            if (arrLength < 0 || arrLength > MAX_VAL_LENGTH)
                throw new InvalidDataException($"Byte array length exceeds maximum allowed length: {arrLength}");

            stream.ReadExactly(buffer[..arrLength]);
            return buffer[..arrLength].ToArray();
        }

        private static short ReadInt16Value(Stream stream, Span<byte> buffer)
        {
            stream.ReadExactly(buffer[..2]);
            return BitConverter.ToInt16(buffer[..2]);
        }

        private static ushort ReadUInt16Value(Stream stream, Span<byte> buffer)
        {
            stream.ReadExactly(buffer[..2]);
            return BitConverter.ToUInt16(buffer[..2]);
        }

        private static int ReadInt32Value(Stream stream, Span<byte> buffer)
        {
            stream.ReadExactly(buffer[..4]);
            return BitConverter.ToInt32(buffer[..4]);
        }

        private static uint ReadUInt32Value(Stream stream, Span<byte> buffer)
        {
            stream.ReadExactly(buffer[..4]);
            return BitConverter.ToUInt32(buffer[..4]);
        }

        private static long ReadInt64Value(Stream stream, Span<byte> buffer)
        {
            stream.ReadExactly(buffer[..8]);
            return BitConverter.ToInt64(buffer[..8]);
        }

        private static ulong ReadUInt64Value(Stream stream, Span<byte> buffer)
        {
            stream.ReadExactly(buffer[..8]);
            return BitConverter.ToUInt64(buffer[..8]);
        }

        private static float ReadFloatValue(Stream stream, Span<byte> buffer)
        {
            stream.ReadExactly(buffer[..4]);
            return BitConverter.ToSingle(buffer[..4]);
        }

        private static double ReadDoubleValue(Stream stream, Span<byte> buffer)
        {
            stream.ReadExactly(buffer[..8]);
            return BitConverter.ToDouble(buffer[..8]);
        }

        private static decimal ReadDecimalValue(Stream stream, Span<byte> buffer)
        {
            stream.ReadExactly(buffer[..16]);
            return buffer[..16].ToArray().ToDecimal();
        }

        private static Guid ReadGuidValue(Stream stream, Span<byte> buffer)
        {
            stream.ReadExactly(buffer[..16]);
            return new Guid(buffer[..16]);
        }

        public static void WriteKeyData(Stream ms, string key, Span<byte> buffer)
        {
            var size = Encoding.UTF8.GetBytes(key, buffer[2..]); //  => buffer.Slice(2);

            if (!BitConverter.TryWriteBytes(buffer, (ushort)size)) // 2 bytes for length
                throw new InvalidOperationException("Failed to write key length to buffer.");

            ms.Write(buffer[..(size + 2)]); //  => buffer.Slice(0, size + 2);
        }

        public static void WriteValueData(Stream ms, object value, Span<byte> buffer)
        {
            if (value is Map m)
            {
                // 直接写入流 
                ms.WriteByte((byte)MapValueType.Map);
                m.SerializeTo(ms);
                return;
            }

            int totalSize;

            if (value is string str)
            {
                buffer[0] = (byte)MapValueType.String;
                var bytesSize = Encoding.UTF8.GetBytes(str, buffer[3..]);
                BitConverter.TryWriteBytes(buffer[1..], (ushort)bytesSize); // 2 bytes for length 

                totalSize = bytesSize + 3;
            }

            else if (value is int i)
            {
                buffer[0] = (byte)MapValueType.Int32;
                BitConverter.TryWriteBytes(buffer[1..], i);

                totalSize = 5;
            }

            else if (value is Guid guid)
            {
                buffer[0] = (byte)MapValueType.Guid;
                guid.TryWriteBytes(buffer[1..]);

                totalSize = 17;
            }

            else if (value is long l)
            {
                buffer[0] = (byte)MapValueType.Int64;
                BitConverter.TryWriteBytes(buffer[1..], l);

                totalSize = 9;
            }

            else if (value is byte[] bytes)
            {
                buffer[0] = (byte)MapValueType.ByteArray;
                var bytesSize = Math.Min(bytes.Length, MAX_VAL_LENGTH);
                BitConverter.TryWriteBytes(buffer[1..], (ushort)bytesSize);
                bytes.AsSpan(0, bytesSize).CopyTo(buffer[3..]);

                totalSize = bytesSize + 3;
            }

            else if (value is bool b)
            {
                buffer[0] = (byte)MapValueType.Boolean;
                buffer[1] = b ? (byte)1 : (byte)0;

                totalSize = 2;
            }

            else if (value is byte by)
            {
                buffer[0] = (byte)MapValueType.Byte;
                buffer[1] = by;

                totalSize = 2;
            }

            else if (value is short s)
            {
                buffer[0] = (byte)MapValueType.Int16;
                BitConverter.TryWriteBytes(buffer[1..], s);

                totalSize = 3;
            }

            else if (value is ushort us)
            {
                buffer[0] = (byte)MapValueType.UInt16;
                BitConverter.TryWriteBytes(buffer[1..], us);

                totalSize = 3;
            }

            else if (value is uint ui)
            {
                buffer[0] = (byte)MapValueType.UInt32;
                BitConverter.TryWriteBytes(buffer[1..], ui);

                totalSize = 5;
            }

            else if (value is ulong ul)
            {
                buffer[0] = (byte)MapValueType.UInt64;
                BitConverter.TryWriteBytes(buffer[1..], ul);

                totalSize = 9;
            }

            else if (value is float f)
            {
                buffer[0] = (byte)MapValueType.Float;
                BitConverter.TryWriteBytes(buffer[1..], f);

                totalSize = 5;
            }

            else if (value is double d)
            {
                buffer[0] = (byte)MapValueType.Double;
                BitConverter.TryWriteBytes(buffer[1..], d);

                totalSize = 9;
            }

            else if (value is decimal dec)
            {
                buffer[0] = (byte)MapValueType.Decimal;
                dec.WriteToBytes(buffer[1..]);

                totalSize = 17;
            }
            else
            {
                throw new NotSupportedException("Unsupported value type: " + value.GetType().FullName);
            }

            ms.Write(buffer[..totalSize]);
        }
    }
}


partial class Map
{
    public object Clone()
    {
        return new Map(this);
    }

    /// <summary>
    /// 拷贝构造函数，创建一个新的 Map 实例，内容与原始 Map 相同，但没有引用循环。
    /// </summary>
    /// <param name="original"></param>
    public Map(Map original) : this(original._flags)
    {
        foreach (var entry in original._dictionary)
        {
            if (entry.Value is Map nestedMap)
            {
                _dictionary[entry.Key] = new Map(nestedMap); // 递归拷贝
            }
            else
            {
                _dictionary[entry.Key] = entry.Value; // 直接赋值
            }
        }
    }

    /// <summary>
    /// 创建一个Map底层直接引用的只读副本
    /// </summary>
    /// <param name="original"></param>
    /// <param name="flags"></param>
    public Map(Map original, MapFlags flags)
    {
        _dictionary = original._dictionary;
        _flags = flags;
    }
}