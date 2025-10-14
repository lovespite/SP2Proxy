using System.Buffers;
using System.Net.Sockets;
using System.Text;

namespace SP2Proxy.Services.Socks5
{
    public class S5Proxy
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly Func<string, int, Task<bool>> _onConnect;

        public S5Proxy(TcpClient client, Func<string, int, Task<bool>> onConnect)
        {
            _client = client;
            _stream = client.GetStream();
            _onConnect = onConnect;
        }

        public async Task ProcessAsync()
        {
            try
            {
                if (!await HandleMethodNegotiation()) return;
                var (host, port) = await HandleConnectionRequest();
                if (host != null)
                {
                    bool success = await _onConnect(host, port);
                    if (success)
                    {
                        // S5Proxy的职责到此结束，上层逻辑接管流
                        await SendSuccessReplyAsync();
                    }
                    else
                    {
                        await SendFailureReplyAsync(S5Reply.ConnectionRefused);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[S5Proxy] Error: {ex.Message}");
            }
            finally
            {
                // 如果 onConnect 没有接管，则关闭连接
                if (_client.Connected) _client.Close();
            }
        }

        private async Task SendSuccessReplyAsync()
        {
            ReadOnlyMemory<byte> reply = new byte[] { 0x05, (byte)S5Reply.Succeeded, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
            await _stream.WriteAsync(reply);
        }

        private async Task<bool> HandleMethodNegotiation()
        {
            // +----+----------+----------+
            // |VER | NMETHODS | METHODS  |
            // +----+----------+----------+
            // | 1  |    1     | 1 to 255 |
            // +----+----------+----------+
            var buffer = ArrayPool<byte>.Shared.Rent(257);
            try
            {
                var headerMemory = buffer.AsMemory(0, 2);
                await _stream.ReadExactlyAsync(headerMemory); // Read VER and NMETHODS

                if (buffer[0] != 0x05) return false; // Not SOCKS5

                var nmethods = buffer[1];
                var methodsMemory = buffer.AsMemory(0, nmethods);
                await _stream.ReadExactlyAsync(methodsMemory);

                // 我们只支持 NO AUTHENTICATION REQUIRED (0x00)
                if (methodsMemory.Span.IndexOf((byte)0x00) != -1)
                {
                    ReadOnlyMemory<byte> response = new byte[] { 0x05, 0x00 };
                    await _stream.WriteAsync(response);
                    return true;
                }

                ReadOnlyMemory<byte> noAcceptableResponse = new byte[] { 0x05, 0xFF };
                await _stream.WriteAsync(noAcceptableResponse); // No acceptable methods
                return false;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task<(string? Host, int Port)> HandleConnectionRequest()
        {
            // +----+-----+-------+------+----------+----------+
            // |VER | CMD |  RSV  | ATYP | DST.ADDR | DST.PORT |
            // +----+-----+-------+------+----------+----------+
            // | 1  |  1  | X'00' |  1   | Variable |    2     |
            // +----+-----+-------+------+----------+----------+
            var buffer = ArrayPool<byte>.Shared.Rent(262);
            try
            {
                var headerMemory = buffer.AsMemory(0, 4);
                await _stream.ReadExactlyAsync(headerMemory);

                if (buffer[0] != 0x05 || buffer[1] != 0x01) // Not SOCKS5 or not CONNECT
                {
                    await SendFailureReplyAsync(S5Reply.CommandNotSupported);
                    return (null, 0);
                }

                string host;
                var addressType = (S5AddressType)buffer[3];
                switch (addressType)
                {
                    case S5AddressType.IPv4:
                        var ipv4Memory = buffer.AsMemory(0, 4);
                        await _stream.ReadExactlyAsync(ipv4Memory);
                        host = $"{buffer[0]}.{buffer[1]}.{buffer[2]}.{buffer[3]}";
                        break;

                    case S5AddressType.DomainName:
                        var lengthMemory = buffer.AsMemory(0, 1);
                        await _stream.ReadExactlyAsync(lengthMemory);
                        var len = buffer[0];
                        var domainMemory = buffer.AsMemory(0, len);
                        await _stream.ReadExactlyAsync(domainMemory);
                        host = Encoding.ASCII.GetString(domainMemory.Span);
                        break;

                    case S5AddressType.IPv6:
                        var ipv6Memory = buffer.AsMemory(0, 16);
                        await _stream.ReadExactlyAsync(ipv6Memory);
                        var sb = new StringBuilder();
                        var ipv6Span = ipv6Memory.Span;
                        for (int i = 0; i < 16; i += 2)
                        {
                            sb.AppendFormat("{0:x2}{1:x2}:", ipv6Span[i], ipv6Span[i + 1]);
                        }
                        sb.Length--; // remove last ':'
                        host = sb.ToString();
                        break;

                    default:
                        await SendFailureReplyAsync(S5Reply.AddressTypeNotSupported);
                        return (null, 0);
                }

                var portMemory = buffer.AsMemory(0, 2);
                await _stream.ReadExactlyAsync(portMemory);
                int port = (buffer[0] << 8) | buffer[1];

                return (host, port);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task SendFailureReplyAsync(S5Reply reason)
        {
            ReadOnlyMemory<byte> reply = new byte[] { 0x05, (byte)reason, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
            await _stream.WriteAsync(reply);
        }

        enum S5AddressType { IPv4 = 0x01, DomainName = 0x03, IPv6 = 0x04 }
        enum S5Reply { Succeeded = 0x00, GeneralFailure = 0x01, CommandNotSupported = 0x07, AddressTypeNotSupported = 0x08, ConnectionRefused = 0x05 }
    }
}
