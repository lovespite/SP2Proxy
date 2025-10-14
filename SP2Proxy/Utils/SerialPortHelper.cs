using System.IO.Ports;
using SP2Proxy.Core;

namespace SP2Proxy.Utils;

// 串口相关的辅助函数
public static class SerialPortHelper
{
    public static Task ListSerialPortsAsync()
    {
        Console.WriteLine("Available serial ports:");
        string[] portNames = SerialPort.GetPortNames();
        if (portNames.Length == 0)
        {
            Console.WriteLine("  None");
        }
        else
        {
            for (int i = 0; i < portNames.Length; i++)
            {
                Console.WriteLine($"  [{i + 1}] {portNames[i]}");
            }
        }
        return Task.CompletedTask;
    }

    public static async Task<SerialPort2[]> OpenSerialPortsAsync(string[] portIdentifiers, int[] baudRates)
    {
        var ports = new List<SerialPort2>();
        var availablePorts = SerialPort.GetPortNames();

        for (int i = 0; i < portIdentifiers.Length; i++)
        {
            string portName = portIdentifiers[i];
            if (portName == ".")
            {
                portName = availablePorts.FirstOrDefault() ?? throw new Exception("No serial port available.");
            }

            int baudRate = (i < baudRates.Length) ? baudRates[i] : baudRates.LastOrDefault(115200);

            var serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
            try
            {
                await Task.Run(serialPort.Open); // 在线程池中打开
                ports.Add(new SerialPort2(serialPort));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open port {portName}: {ex.Message}");
                throw;
            }
        }
        return [.. ports];
    }
}
