using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;
using SP2Proxy.Utils;

namespace SP2Proxy.Core;

public delegate Task FrameReceivedHandler(SerialPort2 port, Frame frame);

// 物理串口的抽象，负责底层数据收发和帧处理
public class SerialPort2 : IDisposable
{
    private readonly SerialPort _baseport;

    private readonly ConcurrentQueue<Frame> _incomingQueue = new();
    private readonly ConcurrentQueue<Frame> _outgoingQueue = new();
    private readonly ConcurrentQueue<Frame> _controlQueue = new();

    //private readonly FrameParser _parser = new();
    //private readonly ConcurrentQueue<IReadOnlyList<byte[]>> _frameBytes = new();

    private readonly FramePipeline _pipe = new();

    private CancellationTokenSource _cancellationTokenSource = new();

    public event FrameReceivedHandler OnFrameReceived;

    public string Path => _baseport.PortName;
    public int BaudRate => _baseport.BaudRate;
    public int BackPressure => _outgoingQueue.Count;

    private bool _isStarted = false;

    public ulong TrafficIn { get; private set; }
    public ulong TrafficOut { get; private set; }

    public ulong FramesIn { get; private set; }
    public ulong FramesOut { get; private set; }


    private Task TrafficInCount(SerialPort2 port, Frame frame)
    {
        TrafficIn += (ulong)frame.Payload.Length;
        return Task.CompletedTask;
    }

    public SerialPort2(SerialPort baseport)
    {
        _baseport = baseport;
        _baseport.ReadTimeout = 500;
        _baseport.WriteTimeout = 500;
        OnFrameReceived += TrafficInCount;
        Console.WriteLine($"[PPH] Port opened: {Path} @ {BaudRate}bps");
    }

    public void Start()
    {
        if (_isStarted) return;

        if (_cancellationTokenSource.IsCancellationRequested)
            _cancellationTokenSource = new CancellationTokenSource();

        _isStarted = true;

        Task.Run(ReceiveDataAsync, _cancellationTokenSource.Token);
        Task.Run(ParseFramesAsync, _cancellationTokenSource.Token);
        Task.Run(DispatchFramesAsync, _cancellationTokenSource.Token);

        Task.Run(SendDataAsync, _cancellationTokenSource.Token);
    }

    // 异步接收数据
    private async Task ReceiveDataAsync()
    {
        var incomingBuffer = new byte[Frame.StackBufferSize].AsMemory();
        while (_baseport.IsOpen)
        {
            try
            {
                _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                if (_baseport.BytesToRead <= 0)
                {
                    await Task.Delay(1, _cancellationTokenSource.Token); // 没有数据时稍作等待
                    continue;
                }

                int bytesRead = await _baseport.BaseStream.ReadAsync(incomingBuffer, _cancellationTokenSource.Token);
                if (bytesRead <= 0) continue;

                //var frameBytes = _parser.Parse(incomingBuffer.Span[..bytesRead]);
                //_frameBytes.Enqueue(frameBytes);

                await _pipe.Writer.WriteAsync(incomingBuffer[..bytesRead], _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[PPH] Receive task cancelled for {Path}");
                break;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("aborted"))
            {
                Console.WriteLine($"[PPH] I/O operation aborted on {Path}: {ex.Message}");
                break;
            }
            catch (IOException)
            {
                // 检查串口状态
                if (!_baseport.IsOpen) break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PPH] Unexpected receive error on {Path}: {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(10, _cancellationTokenSource.Token); // 发生错误时稍作等待
            }
        }

        await _pipe.Writer.CompleteAsync();
        Console.WriteLine($"[PPH] Receive task ended for {Path}");
    }

    private async Task ParseFramesAsync()
    {
        await foreach (var frame in _pipe.ParseFramesAsync())
        {
            if (_cancellationTokenSource.IsCancellationRequested) break;

            try
            {
                _incomingQueue.Enqueue(Frame.Parse(frame.Span));
                ++FramesIn;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PPH] Error parsing frame on {Path}: {ex.Message}");
            }
        }
    }

    //private async Task ParseFramesAsync()
    //{
    //    while (!_cancellationTokenSource.IsCancellationRequested)
    //    {
    //        if (!_frameBytes.TryDequeue(out var frames))
    //        {
    //            await Task.Delay(1); // 队列为空时短暂等待
    //            continue;
    //        }

    //        foreach (var frame in frames)
    //        {
    //            try
    //            {
    //                _incomingQueue.Enqueue(Frame.Parse(frame));
    //                ++FramesIn;
    //            }
    //            catch (Exception ex)
    //            {
    //                Console.WriteLine($"[PPH] Error parsing frame on {Path}: {ex.Message}");
    //            }
    //        }
    //    }
    //}

    private async Task DispatchFramesAsync()
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            if (!_incomingQueue.TryDequeue(out var frame))
            {
                await Task.Delay(1); // 队列为空时短暂等待
                continue;
            }

            if (OnFrameReceived is null)
            {
                continue;
            }

            try
            {
                await OnFrameReceived.Invoke(this, frame);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PPH] Error dispatching frame on {Path}: {ex.Message}");
            }
        }
    }

    // 异步发送数据
    private async Task SendDataAsync()
    {
        Memory<byte> outgoingBuffer = new byte[Frame.StackBufferSize];
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            if (!_controlQueue.TryDequeue(out var frame) // 优先发送控制消息
                && !_outgoingQueue.TryDequeue(out frame))
            {
                await Task.Delay(1); // 队列为空时短暂等待
                continue;
            }

            try
            {
                // 将帧数据封包到发送缓冲区
                var size = FrameUtils.Pack(frame, outgoingBuffer.Span);

                // 发送数据
                await _baseport.BaseStream.WriteAsync(outgoingBuffer[..size], _cancellationTokenSource.Token);
                await _baseport.BaseStream.FlushAsync(_cancellationTokenSource.Token);

                TrafficOut += (ulong)size;
                ++FramesOut;
            }
            catch (IOException)
            {
                if (!_baseport.IsOpen) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PPH] Unexpected send error on {Path}: {ex.GetType().Name}: {ex.Message}");

                await Task.Delay(1, _cancellationTokenSource.Token);
            }
        }
    }

    public void EnqueueOut(Frame frame)
    {
        _outgoingQueue.Enqueue(frame);
    }

    public void EnqueueOutControlFrame(Frame ctlFrame)
    {
        _controlQueue.Enqueue(ctlFrame);
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _baseport.Close();
        _baseport.Dispose();
    }
}
