/*
 ▐▄▄▄       ▄▄▄·       .▄▄ · ▄▄▄▄▄ ▄▄▄·  ▄▄· ▄ •▄ 
  ·██▪     ▐█ ▀█ ▪     ▐█ ▀. •██  ▐█ ▀█ ▐█ ▌▪█▌▄▌▪
▪▄ ██ ▄█▀▄ ▄█▀▀█  ▄█▀▄ ▄▀▀▀█▄ ▐█.▪▄█▀▀█ ██ ▄▄▐▀▀▄·
▐▌▐█▌▐█▌.▐▌▐█ ▪▐▌▐█▌.▐▌▐█▄▪▐█ ▐█▌·▐█ ▪▐▌▐███▌▐█.█▌
 ▀▀▀• ▀█▄▀▪ ▀  ▀  ▀█▄▀▪ ▀▀▀▀  ▀▀▀  ▀  ▀ ·▀▀▀ ·▀  ▀
 github.com/joaostack
*/

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SharpPcap;
using PacketDotNet;
using SharpPcap.LibPcap;

namespace PacketGenerator;

class Program
{
    static void Main(string[] args)
    {
        AnsiConsole.Write(new FigletText("PacketGenerator").Color(Color.Magenta));
        var app = new CommandApp<CommandHandler>();
        app.Configure(config =>
        {
            config.SetApplicationName("PacketGenerator");
            config.SetApplicationVersion("1.0.0");
        });
        app.Run(args);
    }
}

// Type of Protocols enum
enum Protocols
{
    tcp,
    udp
}

/// <summary>
/// Command line configuration class
/// </summary>
public class ConsoleSettings : CommandSettings
{
    [CommandArgument(0, "<PROTOCOL>")]
    [Description("TCP, UDP.")]
    public required string ProtocolType { get; init; }

    [CommandOption("-v|--verbose")]
    [Description("Verbose mode")]
    public bool Verbose { get; init; }

    [CommandOption("-i|--interface", isRequired: true)]
    [Description("Interface name")]
    public required string InterfaceName { get; init; }

    [CommandOption("--dstIp", isRequired: true)]
    [Description("The destination IP address")]
    public required string DstIP { get; init; }

    [CommandOption("--dstPort", isRequired: true)]
    [Description("The destination port")]
    public required ushort DstPort { get; init; }

    [CommandOption("--srcIp")]
    [Description("The source IP address")]
    public string? SrcIP { get; init; }

    [CommandOption("--srcPort")]
    [Description("The source port")]
    public ushort SrcPort { get; init; }

    [CommandOption("-c|--count")]
    [Description("Packet count to send")]
    public int PacketCount { get; init; }
}

/// <summary>
/// Class to handle the commands
/// </summary>
public class CommandHandler : Command<ConsoleSettings>
{
    public override int Execute(CommandContext context, ConsoleSettings settings, CancellationToken cancellation)
    {
        try
        {
            // -- open interface
            if (!string.IsNullOrEmpty(settings.InterfaceName))
            {
                var device = DeviceHelpers.SelectOpenDevice(settings.InterfaceName);

                var dstIp = IPAddress.Parse(settings.DstIP);
                var dstPort = settings.DstPort;
                var srcIp = settings.SrcIP != null ? IPAddress.Parse(settings.SrcIP) : IPAddress.Any;
                var srcPort = settings.SrcPort <= 0 ? new Random().Next(10000, 65535) : settings.SrcPort;
                var verbose = settings.Verbose;
                var packetCount = settings.PacketCount;

                if (string.Equals(settings.ProtocolType.ToLower(), Protocols.tcp.ToString()))
                {
                    PacketBuilder.GenTCPPacket(device, srcIp, dstIp, (ushort)srcPort, dstPort, packetCount, verbose);
                }
                if (string.Equals(settings.ProtocolType.ToLower(), Protocols.udp.ToString()))
                {
                    PacketBuilder.GenUDPPacket(device, srcIp, dstIp, (ushort)srcPort, dstPort, packetCount, verbose);
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
        }

        return 0;
    }
}

/// <sumamry>
/// Class with packet implementation logic
/// </summary>
public class PacketBuilder
{
    /// <summary>
    /// Create a basic TCP packet
    /// </summary>
    public static void GenTCPPacket(ILiveDevice device, IPAddress ipSrc, IPAddress ipDst, ushort srcPort, ushort dstPort, int packetCount, bool verbose = false)
    {
        try
        {
            ipSrc = ipSrc != null ? ipSrc : DeviceHelpers.GetLocalIP(device);

            PhysicalAddress unknownMac = PhysicalAddress.Parse("00-00-00-00-00-00");
            PhysicalAddress sourceMac = device.MacAddress ?? throw new Exception("No MAC found for this interface, try switch.");
            PhysicalAddress targetMac = PacketBuilder.GetMacByIP(device, ipDst) ?? unknownMac;

            // -- Create EthernetPacket
            var ethernetPacket = new EthernetPacket(sourceMac, targetMac, EthernetType.IPv4);

            // -- Create a TCP Packet base
            var tcpPacket = new TcpPacket(srcPort, dstPort);
            tcpPacket.WindowSize = 8192;
            tcpPacket.SequenceNumber = (uint)new Random().Next();

            // -- Create a IP Packet base
            var ipPacket = new IPv4Packet(ipSrc, ipDst);
            ipPacket.TimeToLive = 64;
            ipPacket.PayloadPacket = tcpPacket;

            // -- Update checksums
            ipPacket.CalculateIPChecksum();
            ipPacket.UpdateIPChecksum();
            tcpPacket.CalculateTcpChecksum();
            tcpPacket.UpdateTcpChecksum();

            if (verbose)
            {
                AnsiConsole.MarkupLine($"[cyan][[VERBOSE]][/] IP Checksum [green]{ipPacket.Checksum}[/] - Len [green]{ipPacket.Bytes.Length}[/]");
                AnsiConsole.MarkupLine($"[cyan][[VERBOSE]][/] TCP Checksum [green]{tcpPacket.Checksum}[/] - Len [green]{tcpPacket.Bytes.Length}[/]");

                if (targetMac != unknownMac)
                {
                    AnsiConsole.MarkupLine($"[cyan][[VERBOSE]][/] [green]{ipDst}[/] has [green]{DeviceHelpers.FormatMac(targetMac.ToString())}[/]");
                }
            }

            // -- Assign ipPacket to the ethernetPacket
            ethernetPacket.PayloadPacket = ipPacket;

            for (int i = 0; i <= packetCount; i++)
            {
                device.SendPacket(ethernetPacket);

                AnsiConsole.MarkupLine($"[blue][[+]][/] IP DST = [green]{ipDst}:{dstPort}[/] - IP SRC = [green]{ipSrc}:{srcPort}[/]");
                AnsiConsole.MarkupLine($"[blue][[+]][/] [yellow]{DateTime.UtcNow}[/] TCP packet sent!");
            }
        }
        catch
        {
            throw;
        }
    }

    /// <summary>
    /// Create a basic UDP Packet
    /// </summary>
    public static void GenUDPPacket(ILiveDevice device, IPAddress ipSrc, IPAddress ipDst, ushort srcPort, ushort dstPort, int packetCount, bool verbose = false)
    {
        try
        {
            ipSrc = ipSrc != null ? ipSrc : DeviceHelpers.GetLocalIP(device);

            PhysicalAddress unknownMac = PhysicalAddress.Parse("00-00-00-00-00-00");
            PhysicalAddress sourceMac = device.MacAddress ?? throw new Exception("No MAC found for this interface, try switch.");
            PhysicalAddress targetMac = PacketBuilder.GetMacByIP(device, ipDst) ?? unknownMac;

            // -- Create EthernetPacket
            var ethernetPacket = new EthernetPacket(sourceMac, targetMac, EthernetType.IPv4);
            // -- Create a UDP Packet base
            var udpPacket = new UdpPacket(srcPort, dstPort);
            // -- Create a IP Packet base
            var ipPacket = new IPv4Packet(ipSrc, ipDst);
            ipPacket.TimeToLive = 64;
            ipPacket.PayloadPacket = udpPacket;

            // -- Update checksums
            ipPacket.CalculateIPChecksum();
            ipPacket.UpdateIPChecksum();
            udpPacket.CalculateUdpChecksum();
            udpPacket.UpdateUdpChecksum();

            if (verbose)
            {
                AnsiConsole.MarkupLine($"[cyan][[VERBOSE]][/] IP Checksum [green]{ipPacket.Checksum}[/] - Len [green]{ipPacket.Bytes.Length}[/]");
                AnsiConsole.MarkupLine($"[cyan][[VERBOSE]][/] TCP Checksum [green]{udpPacket.Checksum}[/] - Len [green]{udpPacket.Bytes.Length}[/]");

                if (targetMac != unknownMac)
                {
                    AnsiConsole.MarkupLine($"[cyan][[VERBOSE]][/] [green]{ipDst}[/] has [green]{DeviceHelpers.FormatMac(targetMac.ToString())}[/]");
                }
            }

            // -- Assign IP Packet to the ethernetPacket
            ethernetPacket.PayloadPacket = ipPacket;

            for (int i = 0; i <= packetCount; i++)
            {
                device.SendPacket(ethernetPacket);

                AnsiConsole.MarkupLine($"[blue][[+]][/] IP DST = [green]{ipDst}:{dstPort}[/] - IP SRC = [green]{ipSrc}:{srcPort}[/]");
                AnsiConsole.MarkupLine($"[blue][[+]][/] [yellow]{DateTime.UtcNow}[/] TCP packet sent!");
            }
        }
        catch
        {
            throw;
        }
    }

    /// <summary>
    /// Find to the mac address by IP, using ARP
    /// </summary>
    public static PhysicalAddress GetMacByIP(ILiveDevice device, IPAddress target)
    {
        try
        {
            var localIp = DeviceHelpers.GetLocalIP(device);
            var sourceMac = device.MacAddress ?? throw new Exception("No MAC found for this interface, try switch.");
            PhysicalAddress unknownMac = PhysicalAddress.Parse("00-00-00-00-00-00");
            PhysicalAddress broadccastMac = PhysicalAddress.Parse("FF-FF-FF-FF-FF-FF");

            var ethernetPacket = new EthernetPacket(sourceMac, broadccastMac, EthernetType.None);
            var arpPacket = new ArpPacket(ArpOperation.Request, unknownMac, target, sourceMac, localIp);
            ethernetPacket.PayloadPacket = arpPacket;

            PhysicalAddress targetMac = PhysicalAddress.None;

            PacketArrivalEventHandler handler = (sender, e) =>
            {
                var rawPacket = e.GetPacket();
                var packet = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);
                var arpPacket = packet.Extract<ArpPacket>();

                if (arpPacket != null
                        && arpPacket.Operation == ArpOperation.Response
                        && arpPacket.SenderProtocolAddress.Equals(target))
                {
                    targetMac = arpPacket.SenderHardwareAddress;
                    return;
                }
            };

            device.OnPacketArrival += handler;
            device.StartCapture();
            device.SendPacket(ethernetPacket);
            // -- Wait for arp response...
            Thread.Sleep(1500);

            device.OnPacketArrival -= handler;

            return targetMac ?? throw new Exception("Mac not found for this IP!");
        }
        catch
        {
            throw;
        }
    }
}

public class DeviceHelpers
{
    /// <summary>
    /// Get current network gateway IP address
    /// </summary>
    public static IPAddress GetGateway()
    {
        try
        {
            return NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().GatewayAddresses)
                .Select(g => g?.Address)
                .Where(a => a != null)
                .FirstOrDefault() ?? throw new Exception("No gateway found!");
        }
        catch
        {
            throw;
        }
    }

    /// <summary>
    /// Select network device by name
    /// </summary>
    public static ILiveDevice SelectOpenDevice(string devName)
    {
        try
        {
            var devices = CaptureDeviceList.Instance;
            if (devices.Count < 1)
            {
                throw new InvalidOperationException("No devices found! Please connect a network adapter.");
            }

            LibPcapLiveDeviceList deviceList = LibPcapLiveDeviceList.Instance;

            var device = deviceList
                ?.Where(d => string.Equals(d.Interface?.FriendlyName?.ToLower(), devName.ToLower()))
                ?.FirstOrDefault()
                ?? throw new Exception("Device not found!");

            device.Open(DeviceModes.Promiscuous, 1000);

            return device;
        }
        catch
        {
            throw;
        }
    }

    /// <summary>
    /// Get host IP address by network device
    /// </summary>
    public static IPAddress GetLocalIP(ILiveDevice device)
    {
        try
        {
            return ((SharpPcap.LibPcap.LibPcapLiveDevice)device).Addresses
                    .FirstOrDefault(a => a.Addr?.ipAddress != null && a.Addr.ipAddress.AddressFamily == AddressFamily.InterNetwork)
                    ?.Addr?.ipAddress ?? throw new Exception("Local ip address not found, try switch interface!");
        }
        catch
        {
            throw;
        }
    }

    /// <summary>
    /// Add two dots between mac bytes
    /// </summary>
    public static string FormatMac(string mac)
    {
        var output = string.Join(":", Enumerable.Range(0, 6)
            .Select(i => mac.Substring(i * 2, 2)));

        return output;
    }
}
