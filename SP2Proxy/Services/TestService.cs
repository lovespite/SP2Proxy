using System.Diagnostics;
using System.IO.Ports;
using SP2Proxy.Core;
using SP2Proxy.Utils;

namespace SP2Proxy.Services;

// 'test' 命令的实现，用于串口交互测试
public class TestService
{
    private SerialPort2? _serialPort;
    private CancellationTokenSource _cancellationTokenSource = new();
    private readonly Lock _consoleLock = new();

    public async Task StartAsync(string[] portIdentifiers, int[] baudRates)
    {
        try
        {
            // 设置Ctrl+C处理
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                _cancellationTokenSource.Cancel();
            };

            // 只使用第一个串口进行测试
            var serialPorts = await SerialPortHelper.OpenSerialPortsAsync(
                [.. portIdentifiers.Take(1)],
                [.. baudRates.Take(1)]
                );

            if (serialPorts.Length == 0)
            {
                Console.WriteLine("[TestService] No serial port available for testing.");
                return;
            }

            _serialPort = serialPorts[0];
            _serialPort.OnFrameReceived += OnFrameReceivedAsync;
            _serialPort.Start();

            Console.WriteLine($"[TestService] Started testing on {_serialPort.Path} @ {_serialPort.BaudRate}bps");
            Console.WriteLine("Type messages to send to the serial port. Press Ctrl+C to exit.");
            Console.WriteLine("Commands:");
            Console.WriteLine("  :raw <text>    - Send raw text without frame formatting");
            Console.WriteLine("  :hex <bytes>   - Send hexadecimal bytes (space separated)");
            Console.WriteLine("  :quit          - Exit the test mode");
            Console.WriteLine();

            // 启动用户输入处理
            await ProcessUserInputAsync();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n[TestService] Exiting...");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TestService] Error: {ex.Message}");
        }
        finally
        {
            _serialPort?.Dispose();
        }
    }

    private async Task ProcessUserInputAsync()
    {
        ShowPrompt();

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var input = await ReadLineAsync(_cancellationTokenSource.Token);

                if (string.IsNullOrEmpty(input))
                {
                    ShowPrompt();
                    continue;
                }

                if (input.StartsWith(":"))
                {
                    await ProcessCommandAsync(input);
                }
                else
                {
                    // 默认以帧格式发送消息
                    await SendFramedMessageAsync(input);
                }

                ShowPrompt();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                lock (_consoleLock)
                {
                    Console.WriteLine($"[TestService] Input error: {ex.Message}");
                    ShowPrompt();
                }
            }
        }
    }

    private void ShowPrompt()
    {
        lock (_consoleLock)
        {
            Console.Write("> ");
        }
    }

    private async Task ProcessCommandAsync(string command)
    {
        var parts = command.Split(' ', 2);
        var cmd = parts[0].ToLower();

        switch (cmd)
        {
            case ":quit":
                _cancellationTokenSource.Cancel();
                break;

            case ":raw":
                if (parts.Length > 1)
                {
                    await SendRawMessageAsync(parts[1]);
                }
                else
                {
                    Console.WriteLine("Usage: :raw <text>");
                }
                break;

            case ":hex":
                if (parts.Length > 1)
                {
                    await SendHexMessageAsync(parts[1]);
                }
                else
                {
                    Console.WriteLine("Usage: :hex <bytes> (e.g., :hex 48 65 6C 6C 6F)");
                }
                break;

            case ":help":
                Console.WriteLine("Available commands:");
                Console.WriteLine("  :raw <text>    - Send raw text without frame formatting");
                Console.WriteLine("  :hex <bytes>   - Send hexadecimal bytes (space separated)");
                Console.WriteLine("  :help          - Show this help message");
                Console.WriteLine("  :quit          - Exit the test mode");
                Console.WriteLine("  <text>         - Send text as framed message (default)");
                break;

            default:
                Console.WriteLine($"Unknown command: {cmd}. Type :help for available commands.");
                break;
        }
    }

    private async Task SendFramedMessageAsync(string message)
    {
        try
        {
            var data = System.Text.Encoding.UTF8.GetBytes(message);
            var frame = Frame.Build(data, 1); // 使用通道ID 1 

            _serialPort?.EnqueueOut(frame);

            lock (_consoleLock)
            {
                Console.WriteLine($"[TX] Sent framed message: {message}");
            }
        }
        catch (Exception ex)
        {
            lock (_consoleLock)
            {
                Console.WriteLine($"[TestService] Send error: {ex.Message}");
            }
        }
    }

    private async Task SendRawMessageAsync(string message)
    {
        try
        {
            var data = System.Text.Encoding.UTF8.GetBytes(message);
            var frame = new Frame { ChannelId = 0, Payload = data };

            _serialPort?.EnqueueOut(frame);

            lock (_consoleLock)
            {
                Console.WriteLine($"[TX] Sent raw message: {message}");
            }
        }
        catch (Exception ex)
        {
            lock (_consoleLock)
            {
                Console.WriteLine($"[TestService] Send error: {ex.Message}");
            }
        }
    }

    private async Task SendHexMessageAsync(string hexString)
    {
        try
        {
            var hexBytes = hexString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var data = new byte[hexBytes.Length];

            for (int i = 0; i < hexBytes.Length; i++)
            {
                data[i] = Convert.ToByte(hexBytes[i], 16);
            }

            var frame = new Frame { ChannelId = 0, Payload = data };
            _serialPort?.EnqueueOut(frame);

            lock (_consoleLock)
            {
                Console.WriteLine($"[TX] Sent hex bytes: {BitConverter.ToString(data)}");
            }
        }
        catch (Exception ex)
        {
            lock (_consoleLock)
            {
                Console.WriteLine($"[TestService] Hex send error: {ex.Message}");
            }
        }
    }

    private async Task OnFrameReceivedAsync(SerialPort2 port, Frame frame)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            lock (_consoleLock)
            {
                // 清除当前行
                Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");

                // Console.WriteLine($"[RX {timestamp}] Channel {frame.ChannelId}, Length: {frame.Length}");

                // 尝试以文本形式显示数据
                try
                {
                    var text = System.Text.Encoding.UTF8.GetString(frame.Payload.Span);
                    if (IsReadableText(text))
                    {
                        Console.WriteLine($"[RX] Text: {text}");
                    }
                    else
                    {
                        Console.WriteLine($"[RX] Hex:  {BitConverter.ToString(frame.Payload.ToArray())}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RX] Hex:  {BitConverter.ToString(frame.Payload.ToArray())}");
                }

                ShowPrompt();
            }
        }
        catch (Exception ex)
        {
            lock (_consoleLock)
            {
                Console.WriteLine($"[TestService] Receive processing error: {ex.Message}");
                ShowPrompt();
            }
        }
    }

    private static bool IsReadableText(string text)
    {
        // 简单检查是否为可读文本
        foreach (char c in text)
        {
            if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
                return false;
        }
        return true;
    }

    private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            try
            {

                var line = Console.ReadLine();
                cancellationToken.ThrowIfCancellationRequested();
                return line ?? string.Empty;
            }
            catch (Exception)
            {
                Environment.Exit(1);
                return string.Empty;
            }
        }, cancellationToken);
    }
}