using SP2Proxy.Core;
using SP2Proxy.Utils;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using static SP2Proxy.Core.ControllerChannel;

namespace SP2Proxy.Services;

// 'host' 命令的实现，监听本地端口并将流量转发到串口
public class HostServer
{
    private readonly ChannelManager _channelManager;
    private readonly string _listenAddress;
    private readonly int _port;
    private readonly bool _useSocks5;
    private readonly SerialPort2[] _physicalPorts;

    public HostServer(SerialPort2[] serialPorts, string listenAddress, int port, bool useSocks5)
    {
        _physicalPorts = serialPorts;
        _channelManager = new ChannelManager(serialPorts);
        _listenAddress = listenAddress;
        _port = port;
        _useSocks5 = useSocks5;
    }

    public void Start()
    {
        if (_useSocks5)
        {
            var socksListener = new TcpListener(IPAddress.Parse(_listenAddress), _port);
            socksListener.Start();
            Console.WriteLine($"[HostServer/Socks5] Listening on {_listenAddress}:{_port}");
            _ = AcceptSocksConnectionsAsync(socksListener);
        }

        var httpPort = _useSocks5 ? _port + 1 : _port;
        var httpListener = new TcpListener(IPAddress.Parse(_listenAddress), httpPort);
        httpListener.Start();
        Console.WriteLine($"[HostServer/Http] Listening on {_listenAddress}:{httpPort} for CONNECT requests");
        _ = AcceptHttpConnectionsAsync(httpListener);

        foreach (var item in _physicalPorts)
            item.Start();
    }

    private async Task AcceptSocksConnectionsAsync(TcpListener listener)
    {
        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = HandleSocksConnectionAsync(client);
        }
    }
    private async Task AcceptHttpConnectionsAsync(TcpListener listener)
    {
        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = HandleHttpConnectionAsync(client);
        }
    }

    private async Task HandleHttpConnectionAsync(TcpClient client)
    {
        Console.WriteLine($"[HostServer/Http] New connection from {client.Client.RemoteEndPoint}");
        using var stream = client.GetStream();
        // 简单处理CONNECT请求
        var buffer = new byte[4096];
        var bytesRead = await stream.ReadAsync(buffer);
        var request = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

        var lines = request.Split("\r\n");
        if (lines.Length > 0 && lines[0].StartsWith("CONNECT"))
        {
            var parts = lines[0].Split(' ');
            var hostAndPort = parts[1].Split(':');
            var host = hostAndPort[0];
            var port = int.Parse(hostAndPort[1]);

            Console.WriteLine($"[HostServer/Http] Received request: {host}:{port}");
            await ForwardConnectionAsync(client, host, port, 0);
        }
        else
        {
            client.Close();
        }
    }

    private async Task HandleSocksConnectionAsync(TcpClient client)
    {
        Console.WriteLine($"[HostServer/Socks5] New connection from {client.Client.RemoteEndPoint}");
        var socksProxy = new Socks5.S5Proxy(client, async (host, port) =>
        {
            await ForwardConnectionAsync(client, host, port, 5);
            return true; // 表示连接已处理
        });
        await socksProxy.ProcessAsync();
    }

    private static readonly byte[] HttpSuccessResponse = System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n");

    private async Task ForwardConnectionAsync(TcpClient client, string host, int port, byte version)
    {

        Core.Channel? channel = null;

        try
        {
            var msg = ControlMessage.Command(ControlMessage.Commands.Establish);
            var response = await _channelManager.Controller.CallRemoteProcAsync(msg);
            var cid = (long)response.Data;
            channel = _channelManager.NewChannel(cid);

            if (cid == -1) throw new Exception("Failed to get channel ID from remote.");


            Console.WriteLine($"[Channel/Socket] {channel.Path} <{channel.Cid:x8}> chn. established for {host}:{port}.");

            msg = ControlMessage.Command(ControlMessage.Commands.Connect).SetData(cid);
            msg.Set("host", host);
            msg.Set("port", port);
            msg.Set("v", version);

            // 发送连接请求
            await _channelManager.Controller.CallRemoteProcAsync(msg);
            var cstream = client.GetStream();

            // 响应客户端（SOCKS5由S5Proxy处理，HTTP需要在这里响应）
            if (version == 0)
            {
                await cstream.WriteAsync(HttpSuccessResponse);
            }

            await Task.WhenAll(
                cstream.Pipe(channel), 
                channel.Pipe(cstream)
            );

            await channel.CloseAsync();

            Console.WriteLine($"[Channel/Socket] Pipe closed for {host}:{port}.");
        }
        catch (Exception ex)
        {
            client.Close();
            channel?.CloseAsync();

            Console.WriteLine($"[HostServer] Forwarding failed for {host}:{port}. {ex.Message}");
        }
    }
}
