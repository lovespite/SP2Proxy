using SP2Proxy.Utils;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SP2Proxy.Core;

public delegate Task ConnectRequestHandler(ControlMessage msg);

// 控制信道，用于在两端之间传递命令
public class ControllerChannel : Channel
{
    private readonly IChannelFactory _factory;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ControlMessage>> _pendingRpcs = new();

    public ControllerChannel(SerialPort2 host, IChannelFactory factory) : base(0, host, (c, code) => { })
    {
        _factory = factory;
    }

    public async Task ProcessCtlMessageInternalAsync(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            var msg = ControlMessage.From(bytes.Span);

            if (msg.Flag == ControlMessage.Flags.Callback)
            {
                if (_pendingRpcs.TryRemove(msg.Tk, out var tcs))
                {
                    tcs.SetResult(msg);
                }

                return;
            }

            await HandleControlMessageAsync(msg);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Controller] Error processing message: {ex.Message}");
        }
    }

    private async Task HandleControlMessageAsync(ControlMessage msg)
    {
        var response = ControlMessage.Callback(msg.Tk);

        switch (msg.Cmd)
        {
            case ControlMessage.Commands.Establish:
                var newChannel = _factory.NewChannel();
                response.Data = newChannel.Cid;
                break;
            case ControlMessage.Commands.Connect:
                if (OnConnectRequest is not null)
                    await OnConnectRequest.Invoke(msg);
                break;
            default:
                Console.WriteLine($"[Controller] Unknown command: {msg.Cmd}");
                return;
        }

        SendCtlMessage(response);
    }

    public ConnectRequestHandler? OnConnectRequest { get; set; }

    public async Task<ControlMessage> CallRemoteProcAsync(ControlMessage msg, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<ControlMessage>();

        _pendingRpcs.TryAdd(msg.Tk, tcs);

        SendCtlMessage(msg);

        using var registration = ct.Register(() => tcs.TrySetCanceled());
        return await tcs.Task;
    }

    private void SendCtlMessage(ControlMessage msg)
    {
        using var ms = new MemoryStream();
        msg.Lock().SerializeTo(ms);

        /* 控制信道的 Cid 恒为 0 */
        _host.EnqueueOutControlFrame(Frame.Build(ms.ToArray(), 0));
    }
}
