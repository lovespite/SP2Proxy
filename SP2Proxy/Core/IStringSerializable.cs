namespace SP2Proxy.Core;

public interface IStringSerializable
{
    public ReadOnlySpan<byte> ToBinaryData();
    public string SerializeAsBase64();
    public void SerializeTo(Stream stream);
}