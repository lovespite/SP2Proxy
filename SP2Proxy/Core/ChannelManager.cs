using System.Collections.Concurrent;

namespace SP2Proxy.Core;

// 管理所有虚拟信道
public class ChannelManager : IChannelFactory
{
    private readonly ConcurrentDictionary<long, Channel> _channels = new();
    private readonly SerialPort2[] _hosts;

    public SerialPort2[] Hosts => _hosts;
    public ControllerChannel Controller { get; }

    public ChannelManager(SerialPort2[] hosts)
    {
        _hosts = hosts;
        Controller = new ControllerChannel(hosts[0], this);
        foreach (var host in hosts)
        {
            host.OnFrameReceived += DispatchFramesAsync;
        }
    }

    public ChannelManager(SerialPort2 primaryHost) : this([primaryHost])
    {
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

    private async Task DispatchFramesAsync(SerialPort2 port, Frame frame)
    {
        if (frame.ChannelId == 0)
        {
            // 不要等待控制消息的处理完成，否则会阻塞数据帧的处理
            _ = Controller.ProcessCtlMessageInternalAsync(frame.Payload);
            return;
        }

        if (_channels.TryGetValue(frame.ChannelId, out var channel))
        {
            await channel.PushExternalDataAsync(frame.Payload);
        }
        else
        {
            Console.WriteLine($"[ChannelRouter] Frame dropped: Channel <{frame.ChannelId}> not found.");
        }
    }

    private static long _globalCid = 1;
    private static readonly ConcurrentQueue<long> _cidPool = new();
    private static long NextCid()
    {
        if (_cidPool.TryDequeue(out var cid)) return cid;
        return Interlocked.Increment(ref _globalCid);
    }

    public Channel NewChannel(long cid)
    {
        var channel = new Channel(cid, BestHost, Kill);
        _channels.TryAdd(cid, channel);
        return channel;
    }

    public Channel NewChannel() => NewChannel(NextCid());

    public void Kill(Channel channel, int code)
    {
        if (_channels.TryRemove(channel.Cid, out _))
        {
            _cidPool.Enqueue(channel.Cid); // 回收ID
            Console.WriteLine($"[ChnMan] Channel <{channel.Cid}> destroyed with code 0x{code:X}.");
        }
    }

    public Channel? Get(long id) => _channels.GetValueOrDefault(id);
}
