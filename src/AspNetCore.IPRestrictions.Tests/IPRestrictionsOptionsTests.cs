using System.Net;
using AspNetCore.IPRestrictions.Endpoints;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
#if NET11_0_OR_GREATER
using IPNetwork = System.Net.IPNetwork;
#else
using IPNetwork = AspNetCore.IPRestrictions.Endpoints.IPNetwork;
#endif

namespace AspNetCore.IPRestrictions.Tests;

public class IPRestrictionsOptionsTests
{
    private static readonly string[] LanNetworks =
    [
        "10.0.0.0/8", "::ffff:10.0.0.0/104",
        "172.16.0.0/12", "::ffff:172.16.0.0/108",
        "192.168.0.0/16", "::ffff:192.168.0.0/112"
    ];

    private static readonly string[] LoopbackNetworks = ["127.0.0.0/8", "::1/128"];

    [Fact]
    public void BoundConfigurationChangesShouldBePickedUp()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IPRestrictions:AllowLoopbackIPs"] = "false",
                ["IPRestrictions:AllowedIPs:0"] = "1.2.3.4"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddIPRestrictions("IPRestrictions");
        using var provider = services.BuildServiceProvider();

        // The middleware reads IOptionsMonitor.CurrentValue on every request
        var monitor = provider.GetRequiredService<IOptionsMonitor<IPRestrictionsOptions>>();
        monitor.CurrentValue.AllowedIPAddresses.Should().Equal(IPAddress.Parse("1.2.3.4"));
        monitor.CurrentValue.AllowedIPNetworks.Should().BeEmpty();

        var changes = 0;
        using var _ = monitor.OnChange(_ => changes++);

        // Simulate a dynamic configuration source (file watcher, Azure App Configuration, etc.) reloading
        configuration["IPRestrictions:AllowedIPs:0"] = "5.6.7.8";
        configuration["IPRestrictions:AllowedIPs:1"] = "10.0.0.0/8";
        configuration.Reload();

        changes.Should().BeGreaterThan(0, "a configuration reload should notify options listeners");

        // The new values are bound, and ParseIPAddresses has run again
        var options = monitor.CurrentValue;
        options.AllowedIPs.Should().Equal("5.6.7.8", "10.0.0.0/8");
        options.AllowedIPAddresses.Should().Equal(IPAddress.Parse("5.6.7.8"));
        options.AllowedIPNetworks.Should().Equal(IPNetwork.Parse("10.0.0.0/8"));
    }

    [Fact]
    public void ParseIPAddressesShouldSplitAddressesAndNetworksAndSkipInvalidEntries()
    {
        var options = new IPRestrictionsOptions
        {
            AllowLoopbackIPs = false,
            AllowedIPs =
            [
                "1.2.3.4",
                "2001:db8::1",
                "198.51.100.0/24",
                "2001:db8::/32",
                "not-an-ip",
                "1.2.3.4.5",
                "10.0.0.0/33"   // Prefix too long for IPv4
            ]
        };
        var allowedIPs = options.AllowedIPs.ToList();
        var logger = new ListLogger<IPRestrictionsOptions>();

        options.ParseIPAddresses(logger);

        options.AllowedIPAddresses.Should().BeEquivalentTo(
            [IPAddress.Parse("1.2.3.4"), IPAddress.Parse("2001:db8::1")]);
        options.AllowedIPNetworks.Should().BeEquivalentTo(
            [IPNetwork.Parse("198.51.100.0/24"), IPNetwork.Parse("2001:db8::/32")]);

        logger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message)
            .Should().HaveCount(3)
            .And.Contain(m => m.Contains("not-an-ip"))
            .And.Contain(m => m.Contains("1.2.3.4.5"))
            .And.Contain(m => m.Contains("10.0.0.0/33"));

        // The configured list itself is left untouched
        options.AllowedIPs.Should().Equal(allowedIPs);
    }

    [Fact]
    public void ParseIPAddressesShouldClearHostBits()
    {
        var options = new IPRestrictionsOptions
        {
            AllowLoopbackIPs = false,
            AllowedIPs = ["172.16.5.4/12"] // Host bits set beyond the prefix
        };
        var logger = new ListLogger<IPRestrictionsOptions>();

        options.ParseIPAddresses(logger);

        // Accepted and normalised to 172.16.0.0/12, as System.Net.IPNetwork does from .NET 10
        // (.NET 8 and 9 reject it, but the backport is used until .NET 11)
        options.AllowedIPNetworks.Should().BeEquivalentTo([IPNetwork.Parse("172.16.0.0/12")]);
        logger.Entries.Should().NotContain(e => e.Message.Contains("172.16.5.4/12"));
    }

    [Fact]
    public void ParseIPAddressesShouldRemoveDuplicates()
    {
        var options = new IPRestrictionsOptions
        {
            AllowLanIPs = true,
            AllowLoopbackIPs = true,
            AllowedIPs =
            [
                "1.2.3.4",
                "1.2.3.4",
                "2001:db8::1",
                "2001:0db8:0000::0001", // Same address, different spelling
                "10.0.0.0/8",           // Also added by AllowLanIPs
                "127.0.0.0/8",          // Also added by AllowLoopbackIPs
                "::1/128"               // Also added by AllowLoopbackIPs
            ]
        };

        options.ParseIPAddresses(null);

        options.AllowedIPAddresses.Should().BeEquivalentTo(
            [IPAddress.Parse("1.2.3.4"), IPAddress.Parse("2001:db8::1")]);
        options.AllowedIPNetworks.Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo(LanNetworks.Concat(LoopbackNetworks).Select(IPNetwork.Parse));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ParseIPAddressesShouldAddLanAndLoopbackNetworks(bool allowLanIPs, bool allowLoopbackIPs)
    {
        var options = new IPRestrictionsOptions
        {
            AllowLanIPs = allowLanIPs,
            AllowLoopbackIPs = allowLoopbackIPs,
            AllowedIPs = ["203.0.113.0/24"]
        };

        options.ParseIPAddresses(null);

        IEnumerable<string> expected = ["203.0.113.0/24"];
        if (allowLanIPs)
            expected = expected.Concat(LanNetworks);
        if (allowLoopbackIPs)
            expected = expected.Concat(LoopbackNetworks);

        options.AllowedIPAddresses.Should().BeEmpty();
        options.AllowedIPNetworks.Should().BeEquivalentTo(expected.Select(IPNetwork.Parse));
    }

    [Theory]
    [InlineData(IPRestrictionMode.Enabled, LogLevel.Warning)]
    [InlineData(IPRestrictionMode.AddEndpointHeader, LogLevel.Debug)]
    [InlineData(IPRestrictionMode.Disabled, LogLevel.Debug)]
    public void ParseIPAddressesWithNoValidEntriesShouldLog(IPRestrictionMode mode, LogLevel expectedLevel)
    {
        var options = new IPRestrictionsOptions
        {
            Enabled = mode,
            AllowLoopbackIPs = false,
            AllowedIPs = ["invalid"]
        };
        var logger = new ListLogger<IPRestrictionsOptions>();

        options.ParseIPAddresses(logger);

        options.AllowedIPAddresses.Should().BeEmpty();
        options.AllowedIPNetworks.Should().BeEmpty();
        logger.Entries.Should().ContainSingle(e => e.Message.StartsWith("No allowed IP addresses or networks found"))
            .Which.Level.Should().Be(expectedLevel);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

#if NET7_0_OR_GREATER
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
#else
        public IDisposable BeginScope<TState>(TState state) => null!;
#endif
    }
}
