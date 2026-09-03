namespace PowerX.Core.Telemetry;

public sealed record NetworkInterfaceMetrics
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Type { get; init; }          // Ethernet / Wi-Fi / …
    public required bool IsUp { get; init; }
    public required long LinkSpeedBps { get; init; }
    public required double SendBytesPerSec { get; init; }
    public required double ReceiveBytesPerSec { get; init; }
    public required long TotalBytesSent { get; init; }
    public required long TotalBytesReceived { get; init; }
    public required string MacAddress { get; init; }
    public IReadOnlyList<string> IpAddresses { get; init; } = [];
    public IReadOnlyList<string> Gateways { get; init; } = [];
    public IReadOnlyList<string> DnsServers { get; init; } = [];
}

public sealed record NetworkMetrics(
    IReadOnlyList<NetworkInterfaceMetrics> Interfaces,
    DateTimeOffset Timestamp)
{
    public double TotalSendBytesPerSec => Interfaces.Where(i => i.IsUp).Sum(i => i.SendBytesPerSec);
    public double TotalReceiveBytesPerSec => Interfaces.Where(i => i.IsUp).Sum(i => i.ReceiveBytesPerSec);
}
