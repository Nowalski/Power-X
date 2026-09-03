using FluentAssertions;
using PowerX.Core.Telemetry;
using Xunit;

namespace PowerX.Core.Tests;

public class NetworkTests
{
    private static NetworkConnection Conn(string proto, string state, string localAddr, int localPort,
        string? remoteAddr = "93.184.216.34", int remotePort = 443)
    {
        bool listening = state == "LISTEN" || (!proto.StartsWith("TCP") && remoteAddr is null);
        return new NetworkConnection
        {
            Protocol = proto, Pid = 100, ProcessName = "test.exe",
            LocalAddress = localAddr, LocalPort = localPort,
            RemoteAddress = listening ? null : remoteAddr, RemotePort = listening ? 0 : remotePort,
            State = state, IsListening = listening,
            Exposed = listening && !(localAddr.StartsWith("127.") || localAddr == "::1"),
        };
    }

    [Fact]
    public void Summarize_counts_states()
    {
        var conns = new List<NetworkConnection>
        {
            Conn("TCP", "ESTABLISHED", "10.0.0.2", 51000),
            Conn("TCP", "ESTABLISHED", "10.0.0.2", 51001),
            Conn("TCP", "TIME-WAIT", "10.0.0.2", 51002),
            Conn("TCP", "LISTEN", "0.0.0.0", 445),
            Conn("TCP", "CLOSE-WAIT", "10.0.0.2", 51003),
            Conn("UDP", "", "0.0.0.0", 53, remoteAddr: null),
        };

        var s = ConnectionProvider.Summarize(conns);
        s.Total.Should().Be(6);
        s.Established.Should().Be(2);
        s.TimeWait.Should().Be(1);
        s.Listening.Should().Be(2);       // the TCP LISTEN + the owned UDP endpoint
        s.OtherTcp.Should().Be(1);        // CLOSE-WAIT
        s.Udp.Should().Be(1);
    }

    [Fact]
    public void ListeningPorts_returns_only_listeners_deduped_and_sorted()
    {
        var conns = new List<NetworkConnection>
        {
            Conn("TCP", "ESTABLISHED", "10.0.0.2", 51000),
            Conn("TCP", "LISTEN", "0.0.0.0", 445),
            Conn("TCP", "LISTEN", "0.0.0.0", 445),   // duplicate row, same pid+port
            Conn("TCP", "LISTEN", "127.0.0.1", 6463),
        };

        var ports = ConnectionProvider.ListeningPorts(conns);
        ports.Select(p => p.LocalPort).Should().Equal(445, 6463);
        ports[0].Exposed.Should().BeTrue();    // 0.0.0.0
        ports[1].Exposed.Should().BeFalse();   // loopback
    }

    [Theory]
    [InlineData("93.184.216.34", true)]     // public
    [InlineData("8.8.8.8", true)]
    [InlineData("127.0.0.1", false)]        // loopback
    [InlineData("0.0.0.0", false)]          // any
    [InlineData("192.168.1.10", false)]     // private
    [InlineData("10.4.4.4", false)]
    [InlineData("172.16.9.9", false)]
    [InlineData("169.254.1.1", false)]      // link-local
    [InlineData("::1", false)]
    [InlineData("not-an-ip", false)]
    public void ReverseDns_only_resolves_routable_public_addresses(string ip, bool expected)
        => ReverseDns.IsResolvable(ip).Should().Be(expected);
}
