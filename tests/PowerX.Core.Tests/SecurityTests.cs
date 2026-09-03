using System.Text.Json;
using FluentAssertions;
using PowerX.Core.Diagnostics;
using Xunit;

namespace PowerX.Core.Tests;

public class SecurityTests
{
    private static HashResult Parse(string json)
        => HashLookup.Parse(JsonDocument.Parse(json).RootElement);

    [Fact]
    public async Task CheckAsync_rejects_a_string_that_is_not_a_sha256()
    {
        var r = await HashLookup.CheckAsync("not-a-hash");
        r.Found.Should().BeFalse();
        r.Summary.Should().Contain("not a SHA-256");
    }

    [Fact]
    public void Parse_flags_a_known_malicious_hash()
    {
        var r = Parse("""{"FileName":"eicar.com","KnownMalicious":"malshare.com","source":"RDS"}""");
        r.Found.Should().BeTrue();
        r.KnownMalicious.Should().BeTrue();
        r.MaliciousDetail.Should().Be("malshare.com");
        r.Summary.Should().Contain("malicious").And.Contain("antivirus scan");
    }

    [Fact]
    public void Parse_reports_a_trusted_known_good_file()
    {
        var r = Parse("""{"FileName":"kernel32.dll","hashlookup:trust":95,"source":["NSRL","Microsoft"]}""");
        r.Found.Should().BeTrue();
        r.KnownMalicious.Should().BeFalse();
        r.Trust.Should().Be(95);
        r.Sources.Should().Contain("NSRL").And.Contain("Microsoft");
        r.Summary.Should().Contain("Known good");
    }

    [Fact]
    public void Parse_calls_out_a_low_trust_known_file()
    {
        var r = Parse("""{"FileName":"weird.exe","hashlookup:trust":12,"source":"somedb"}""");
        r.Trust.Should().Be(12);
        r.Summary.Should().Contain("low trust").And.Contain("closer look");
    }

    [Fact]
    public void Defender_status_never_throws_and_reports_a_mode()
    {
        var s = Defender.Status();
        s.Should().NotBeNull();
        Enum.IsDefined(s.Mode).Should().BeTrue();
        // On a normal dev box Defender is present; if the provider is missing we still get a Detail.
        (s.Mode != DefenderMode.NotAvailable || s.Detail is not null).Should().BeTrue();
    }

    [Fact]
    public void Defender_threat_history_never_throws()
    {
        var act = () => Defender.ThreatHistory(10);
        act.Should().NotThrow();
    }
}
