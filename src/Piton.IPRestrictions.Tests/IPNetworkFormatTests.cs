using System.Net;
using Piton.IPRestrictions.Endpoints;
using FluentAssertions;
using Xunit;

#if NET11_0_OR_GREATER
using IPNetwork = System.Net.IPNetwork;
#else
using IPNetwork = Piton.IPRestrictions.Endpoints.IPNetwork;
#endif

namespace Piton.IPRestrictions.Tests;

public class IPNetworkFormatTests
{
    [Theory]
    [InlineData("0.0.0.0", 0, "0.0.0.0/0")]
    [InlineData("10.0.0.0", 8, "10.0.0.0/8")]
    [InlineData("172.16.0.0", 12, "172.16.0.0/12")]
    [InlineData("192.168.1.0", 24, "192.168.1.0/24")]
    [InlineData("203.0.113.5", 32, "203.0.113.5/32")]
    [InlineData("::", 0, "::/0")]
    [InlineData("2001:db8::", 32, "2001:db8::/32")]
    [InlineData("2001:0db8:0000:0000::", 64, "2001:db8::/64")] // Compressed when formatted
    [InlineData("fe80::", 10, "fe80::/10")]
    [InlineData("::1", 128, "::1/128")]
    [InlineData("::ffff:192.168.0.0", 112, "::ffff:192.168.0.0/112")] // IPv4 mapped stays mapped
    public void FormatShouldUseCidrNotation(string baseAddress, int prefixLength, string expected)
    {
        var network = new IPNetwork(IPAddress.Parse(baseAddress), prefixLength);

        IPRestrictionsMiddleware.Format(network).Should().Be(expected);
    }
}
