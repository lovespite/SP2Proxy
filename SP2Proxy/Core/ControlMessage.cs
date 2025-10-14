using SP2Proxy.Utils;
using System.IO.Compression;

namespace SP2Proxy.Core;

public class ControlMessage : Map
{
    public static ControlMessage From(ReadOnlySpan<byte> bytes)
    {
        var map = Deserialize(bytes);
        return new ControlMessage(map);
    }

    public enum Flags : byte { Unset = 0, Control = 1, Callback = 2 }
    public enum Commands : byte { Unset, Establish, Dispose, Connect, Request }

    public Guid Tk
    {
        private init => Set(value);
        get => (Guid)Get();
    }

    public Commands Cmd
    {
        set => Set((byte)value);
        get => (Commands)Get();
    }

    public Flags Flag
    {
        set => Set((byte)value);
        get => (Flags)Get();
    }

    public object Data
    {
        set => Set(value);
        get => Get();
    }

    public ControlMessage SetData(object data)
    {
        Data = data;
        return this;
    }

    public ControlMessage(Guid tk, Commands cmd, Flags flag, object? data = null)
    {
        Tk = tk;
        Cmd = cmd;
        Flag = flag;
        Data = data ?? string.Empty;
    }

    public ControlMessage() : this(Guid.NewGuid(), Commands.Unset, Flags.Unset, string.Empty)
    {
    }

    public ControlMessage(Map basemap) : base(basemap, MapFlags.None)
    {
        if (!Has(nameof(Tk)))
            Tk = Guid.NewGuid();

        if (!Has(nameof(Cmd)))
            Cmd = Commands.Unset;

        if (!Has(nameof(Flag)))
            Flag = Flags.Unset;

        if (!Has(nameof(Data)))
            Data = string.Empty;
    }

    public static ControlMessage Callback(Guid tk)
    {
        return new ControlMessage(tk, Commands.Unset, Flags.Callback, string.Empty);
    }

    public static ControlMessage Command(Commands cmd, object? data = null)
    {
        return new()
        {
            Cmd = cmd,
            Flag = Flags.Control,
            Data = data ?? string.Empty
        };
    }
}