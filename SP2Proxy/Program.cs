using System.CommandLine;
using System.IO.Ports;
using SP2Proxy.Services;
using SP2Proxy.Utils;

// 主程序入口点
var serialPortOption = new Option<string[]>(
    name: "--serial-port",
    description: "Specify the serial ports to connect, use comma to separate multiple ports.")
{
    AllowMultipleArgumentsPerToken = true,
    Arity = ArgumentArity.OneOrMore
};
serialPortOption.AddAlias("-s");

var baudRateOption = new Option<int[]>(
    name: "--baud-rate",
    description: "Specify the baud rates for serial ports.")
{
    AllowMultipleArgumentsPerToken = true,
    Arity = ArgumentArity.OneOrMore
};
baudRateOption.AddAlias("-b");

var listenOption = new Option<string>(
    name: "--listen",
    description: "Specify the IP address to listen.",
    getDefaultValue: () => "0.0.0.0");
listenOption.AddAlias("-l");

var portOption = new Option<int>(
    name: "--port",
    description: "Specify the port to listen.",
    getDefaultValue: () => 13808);
portOption.AddAlias("-p");

var socks5Option = new Option<bool>(
    name: "--socks5",
    description: "Enable SOCKS5 protocol.",
    getDefaultValue: () => false);
socks5Option.AddAlias("-5");

// 'list' 命令
var listCommand = new Command("list", "List all available serial ports.");
listCommand.SetHandler(async () =>
{
    await SerialPortHelper.ListSerialPortsAsync();
});

// 'proxy' 命令
var proxyCommand = new Command("proxy", "Start the proxy endpoint (the traffic outlet).");
proxyCommand.AddOption(serialPortOption);
proxyCommand.AddOption(baudRateOption);
proxyCommand.SetHandler(async (ports, baudRates) =>
{
    var serialPorts = await SerialPortHelper.OpenSerialPortsAsync(ports, baudRates);
    var endPoint = new ProxyEndPoint(serialPorts);
    endPoint.Start();
    await Task.Delay(-1); // 保持运行
}, serialPortOption, baudRateOption);

// 'host' 命令
var hostCommand = new Command("host", "Start the intermedia proxy host.");
hostCommand.AddOption(serialPortOption);
hostCommand.AddOption(baudRateOption);
hostCommand.AddOption(listenOption);
hostCommand.AddOption(portOption);
hostCommand.AddOption(socks5Option);
hostCommand.SetHandler(async (ports, baudRates, listen, port, useSocks5) =>
{
    var serialPorts = await SerialPortHelper.OpenSerialPortsAsync(ports, baudRates);
    var hostServer = new HostServer(serialPorts, listen, port, useSocks5);
    hostServer.Start();

    await Task.Delay(-1); // 保持运行
}, serialPortOption, baudRateOption, listenOption, portOption, socks5Option);

// 'test' 命令
var testCommand = new Command("test", "Open a serial port for interactive testing.");
testCommand.AddOption(serialPortOption);
testCommand.AddOption(baudRateOption);
testCommand.SetHandler(async (ports, baudRates) =>
{
    var testService = new TestService();
    await testService.StartAsync(ports, baudRates);
}, serialPortOption, baudRateOption);

// 'test-proxy' 命令
var testProxyCommand = new Command("test-proxy", "Start a test proxy with a single serial port.");
testProxyCommand.AddOption(serialPortOption);
testProxyCommand.AddOption(baudRateOption);
testProxyCommand.AddOption(listenOption);
testProxyCommand.AddOption(portOption);
testProxyCommand.AddOption(socks5Option);

testProxyCommand.SetHandler(async (ports, baudRates, listen, port, useSocks5) =>
{
    var spIn = await SerialPortHelper.OpenSerialPortsAsync([ports[0]], [baudRates[0]]);
    var spOut = await SerialPortHelper.OpenSerialPortsAsync([ports[1]], [baudRates[1]]);

    var hostServer = new HostServer(spIn, listen, port, useSocks5);
    var proxyEndPoint = new ProxyEndPoint(spOut);

    hostServer.Start();
    proxyEndPoint.Start();

    await Task.Delay(-1); // 保持运行 

}, serialPortOption, baudRateOption, listenOption, portOption, socks5Option);

var rootCommand = new RootCommand("Serial Port Proxy in C#");
rootCommand.AddCommand(listCommand);
rootCommand.AddCommand(testCommand);
rootCommand.AddCommand(proxyCommand);
rootCommand.AddCommand(hostCommand);
rootCommand.AddCommand(testProxyCommand);

return await rootCommand.InvokeAsync(args);
