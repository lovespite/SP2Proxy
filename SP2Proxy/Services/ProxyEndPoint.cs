using System.Net.Sockets;
using System.Text.Json;
using SP2Proxy.Core;
using static SP2Proxy.Core.ControllerChannel;

namespace SP2Proxy.Services;

// 'proxy' 命令的实现，作为流量出口
public class ProxyEndPoint
{
    private readonly ChannelManager _channelManager;

    public ProxyEndPoint(SerialPort2[] serialPorts)
    {
        _channelManager = new ChannelManager(serialPorts);
    }

    public void Start()
    {
        _channelManager.Controller.OnConnectRequest = HandleConnectRequestAsync;

        foreach (var item in _channelManager.Hosts)
            item.Start();

        Console.WriteLine("[ProxyEndPoint] Started. Waiting for connection requests...");
    }

    private async Task HandleConnectRequestAsync(ControlMessage msg)
    {
        try
        {
            var cid = (long)msg.Data;
            var host = msg.Get<string>("host").Value;
            var port = msg.Get<int>("port").Value;

            var channel = _channelManager.Get(cid);
            if (channel is null)
            {
                Console.WriteLine($"[ProxyEndPoint] Channel not found: {cid}");
                return;
            }

            Console.WriteLine($"[ProxyEndPoint] Connecting to {host}:{port} for channel <{cid}>");

            var remoteClient = new TcpClient();
            await remoteClient.ConnectAsync(host, port);

            Console.WriteLine($"[ProxyEndPoint] Connected to {host}:{port}");

            var cstream = remoteClient.GetStream();

            _ = channel.Pipe(cstream);
            _ = cstream.Pipe(channel);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProxyEndPoint] Failed to handle connect request: {ex.Message}");
        }
    }
}
