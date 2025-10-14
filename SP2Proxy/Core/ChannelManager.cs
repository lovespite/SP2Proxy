using System.Collections.Concurrent;

namespace SP2Proxy.Core;

// 管理所有虚拟信道
public class ChannelManager : IChannelFactory
{
    private readonly ConcurrentDictionary<long, Channel> _channels = new();
    private readonly SerialPort2[] _hosts;

    public SerialPort2[] Hosts => _hosts;
    public ControllerChannel Controller { get; }

    public ChannelManager(SerialPort2 primaryHost)
    {
        _hosts = [primaryHost];
        Controller = new ControllerChannel(primaryHost, this);
        primaryHost.OnFrameReceived += DispatchFrameAsync;
    }

    public ChannelManager(SerialPort2[] hosts)
    {
        _hosts = hosts;
        Controller = new ControllerChannel(hosts[0], this);
        foreach (var host in hosts)
        {
            host.OnFrameReceived += DispatchFrameAsync;
        }
    }

    private SerialPort2 BestHost
    {
        get
        {
            if (_hosts.Length == 1) return _hosts[0];
            // 简单的负载均衡：选择队列最短的主机
            return _hosts.OrderBy(h => h.BackPressure).First();
        }
    }

    private async Task DispatchFrameAsync(SerialPort2 port, Frame frame)
    {
        if (frame.ChannelId == 0)
        {
            await Controller.ProcessCtlMessageInternalAsync(frame.Payload);
            return;
        }

        if (_channels.TryGetValue(frame.ChannelId, out var channel))
        {
            await channel.PushExternalDataAsync(frame.Payload);
        }
        else
        {
            Console.WriteLine($"[ChnMan] Frame dropped: Channel <{frame.ChannelId}> not found.");
        }
    }

    public Channel NewChannel(long? id = null)
    {
        long cid = id ?? ((long)DateTime.UtcNow.Ticks << 16) ^ Random.Shared.NextInt64(0xFFFF);
        var channel = new Channel(cid, BestHost, Kill);
        _channels.TryAdd(cid, channel);
        return channel;
    }

    public void Kill(Channel channel, int code)
    {
        if (_channels.TryRemove(channel.Cid, out _))
        {
            Console.WriteLine($"[ChnMan] Channel <{channel.Cid}> destroyed with code 0x{code:X}.");
        }
    }

    public Channel? Get(long id) => _channels.GetValueOrDefault(id);
}
