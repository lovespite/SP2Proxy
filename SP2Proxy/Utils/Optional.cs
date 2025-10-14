namespace SP2Proxy.Utils;

public readonly struct Optional<T> where T : notnull
{
    private readonly T? _value;
    public bool HasValue { get; }

    public Optional(T value)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
        HasValue = true;
    }

    // 私有构造函数用于创建空值
    private Optional(bool hasValue)
    {
        _value = default;
        HasValue = hasValue;
    }

    public readonly T Value
    {
        get
        {
            if (!HasValue) throw new InvalidOperationException("No value present");
            return _value!;
        }
    }

    public static implicit operator Optional<T>(T value) => new(value);
    public static Optional<T> Empty() => new(false);

    public T GetValueOrDefault(T defaultValue = default!) => HasValue ? _value! : defaultValue;
}