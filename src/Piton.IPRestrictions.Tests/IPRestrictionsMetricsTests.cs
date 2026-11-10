using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Piton.IPRestrictions.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Xunit;

namespace Piton.IPRestrictions.Tests;

public class IPRestrictionsMetricsTests
{
    internal const string AllowedIP = "203.0.113.5";
    internal const string DeniedIP = "198.51.100.1";
    private const string SkipHeader = "X-Skip-IP-Restrictions";

    private static readonly string[] Instruments =
    [
        IPRestrictionsMetrics.RequestsInstrumentName,
        IPRestrictionsMetrics.AllowedRequestsInstrumentName,
        IPRestrictionsMetrics.DeniedRequestsInstrumentName
    ];

    /// <summary>
    /// Tags measurements with the route. Each test's restricted endpoint has a route unique to it, so its measurements
    /// can be told apart from those of other tests running in parallel (which share the meter before .NET 8).
    /// </summary>
    internal static void TagRoute(ref TagList tags, Endpoint endpoint, IPAddress? remoteIp)
    {
        if (endpoint is RouteEndpoint { RoutePattern.RawText: { } route })
            tags.Add("http.route", route);
    }

    internal static TestWebApp CreateApp(
        int testPort,
        string route,
        Action<IPRestrictionsMetricsOptions>? configureMetrics,
        Action<IServiceCollection>? configureServices = null) =>
        new(testPort: testPort,
            configureOptions: o =>
            {
                o.AllowLoopbackIPs = false;
                o.AllowedIPs.Add("203.0.113.0/24");
                configureMetrics?.Invoke(o.Metrics);
            },
            useForwardedHeaders: true,
            configureApp: app =>
            {
                app.MapGet(route, () => "ok")
                    .RestrictIP(context => !context.Request.Headers.ContainsKey(SkipHeader));
                app.MapGet(route + "-public", () => "public");
            },
            configureServices: configureServices);

    /// <summary>
    /// Makes 2 allowed, 1 denied and 1 exempted (by the predicate) request to the restricted route,
    /// and 1 request to an unrestricted route.
    /// </summary>
    private static async Task MakeRequests(TestWebApp app, string route)
    {
        await app.InvokeGet(route, AllowedIP);
        await app.InvokeGet(route, AllowedIP);
        await app.EnsureFailure(route, DeniedIP);
        await app.InvokeGet(route, DeniedIP, new Dictionary<string, string> { [SkipHeader] = "1" });
        await app.InvokeGet(route + "-public", DeniedIP);
    }

    /// <summary>
    /// Listens to the IP restrictions instruments, with the measurements tagged with <paramref name="route"/>.
    /// </summary>
    private static MeterListener Listen(string route, Action<Instrument, long, Dictionary<string, object?>> onMeasurement)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == IPRestrictionsMetrics.MeterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var tagDictionary = new Dictionary<string, object?>();
            foreach (var tag in tags)
                tagDictionary[tag.Key] = tag.Value;
            if (Equals(tagDictionary.GetValueOrDefault("http.route"), route))
                onMeasurement(instrument, value, tagDictionary);
        });
        listener.Start();
        return listener;
    }

    [Fact]
    public void OnlyDeniedRequestsShouldBeCountedByDefault()
    {
        var options = new IPRestrictionsOptions().Metrics;

        options.TotalRequests.Should().BeFalse();
        options.AllowedRequests.Should().BeFalse();
        options.DeniedRequests.Should().BeTrue();
        options.ConfigureTags.Should().BeNull();
    }

    [Theory]
    [InlineData(null, null, null)] // Defaults: denied requests only
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public async Task EnabledMetricsShouldBeRecorded(bool? totalRequests, bool? allowedRequests, bool? deniedRequests)
    {
        var defaults = new IPRestrictionsMetricsOptions();
        var (total, allowed, denied) = (
            totalRequests ?? defaults.TotalRequests,
            allowedRequests ?? defaults.AllowedRequests,
            deniedRequests ?? defaults.DeniedRequests);

        // Unique per case: tests in a class run one at a time, but other classes run in parallel
        var caseId = (totalRequests is null ? 8 : 0) + (total ? 4 : 0) + (allowed ? 2 : 0) + (denied ? 1 : 0);
        var route = $"/metrics/listener/{caseId}";

        var counts = new ConcurrentDictionary<string, long>();
        using var listener = Listen(route, (instrument, value, _) =>
            counts.AddOrUpdate(instrument.Name, value, (_, existing) => existing + value));

        await using var app = CreateApp(testPort: 40 + caseId, route, metrics =>
        {
            metrics.ConfigureTags = TagRoute;
            if (totalRequests.HasValue)
                metrics.TotalRequests = totalRequests.Value;
            if (allowedRequests.HasValue)
                metrics.AllowedRequests = allowedRequests.Value;
            if (deniedRequests.HasValue)
                metrics.DeniedRequests = deniedRequests.Value;
        });
        await MakeRequests(app, route);

        var expected = new Dictionary<string, long>();
        // Total: 2 allowed, 1 denied and 1 exempted by the predicate. The unrestricted endpoint isn't counted.
        if (total)
            expected[IPRestrictionsMetrics.RequestsInstrumentName] = 4;
        if (allowed)
            expected[IPRestrictionsMetrics.AllowedRequestsInstrumentName] = 2;
        if (denied)
            expected[IPRestrictionsMetrics.DeniedRequestsInstrumentName] = 1;

        counts.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task ConfigureTagsShouldTagMeasurements()
    {
        const string route = "/metrics/configure-tags";
        var measurements = new ConcurrentBag<(string Instrument, object? ClientAddress)>();
        var remoteIps = new ConcurrentBag<IPAddress?>();

        using var listener = Listen(route, (instrument, _, tags) =>
            measurements.Add((instrument.Name, tags.GetValueOrDefault("client.address"))));

        await using var app = CreateApp(testPort: 61, route, metrics =>
        {
            metrics.AllowedRequests = true;
            metrics.ConfigureTags = (ref TagList tags, Endpoint endpoint, IPAddress? remoteIp) =>
            {
                remoteIps.Add(remoteIp);
                TagRoute(ref tags, endpoint, remoteIp);
                if (remoteIp != null)
                    tags.Add("client.address", remoteIp.ToString());
            };
        });
        await app.InvokeGet(route, AllowedIP);
        // An IPv4 client on a dual mode socket: given to the callback as the IPv4 address
        await app.InvokeGet(route, "::ffff:203.0.113.6");
        await app.EnsureFailure(route, DeniedIP);

        remoteIps.Should().OnlyContain(ip => ip != null && ip.AddressFamily == AddressFamily.InterNetwork);
        measurements.Should().BeEquivalentTo(new (string, object?)[]
        {
            (IPRestrictionsMetrics.AllowedRequestsInstrumentName, AllowedIP),
            (IPRestrictionsMetrics.AllowedRequestsInstrumentName, "203.0.113.6"),
            (IPRestrictionsMetrics.DeniedRequestsInstrumentName, DeniedIP)
        });
    }

    [Fact]
    public void MetricsOptionsShouldBindFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IPRestrictions:Metrics:TotalRequests"] = "true",
                ["IPRestrictions:Metrics:AllowedRequests"] = "true",
                ["IPRestrictions:Metrics:DeniedRequests"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddIPRestrictions("IPRestrictions");
        using var provider = services.BuildServiceProvider();

        var metrics = provider.GetRequiredService<IOptions<IPRestrictionsOptions>>().Value.Metrics;
        metrics.TotalRequests.Should().BeTrue();
        metrics.AllowedRequests.Should().BeTrue();
        metrics.DeniedRequests.Should().BeFalse();
        metrics.ConfigureTags.Should().BeNull("a callback is not bound from configuration");
    }

    [Fact]
    public async Task AddIPRestrictionsInstrumentationShouldExportTheMetrics()
    {
        const string route = "/metrics/opentelemetry";
        var exported = new List<Metric>();

        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddIPRestrictionsInstrumentation()
            .AddInMemoryExporter(exported)
            .Build();

        await using var app = CreateApp(testPort: 60, route, metrics =>
        {
            metrics.ConfigureTags = TagRoute;
            metrics.TotalRequests = true;
            metrics.AllowedRequests = true;
            metrics.DeniedRequests = true;
        });
        await MakeRequests(app, route);

        meterProvider.ForceFlush().Should().BeTrue();

        CountExported(exported, route).Should().BeEquivalentTo(new Dictionary<string, long>
        {
            [IPRestrictionsMetrics.RequestsInstrumentName] = 4,
            [IPRestrictionsMetrics.AllowedRequestsInstrumentName] = 2,
            [IPRestrictionsMetrics.DeniedRequestsInstrumentName] = 1,
        });
    }

    [Fact]
    public async Task AddIPRestrictionsInstrumentationShouldConfigureTheOptions()
    {
        const string route = "/metrics/opentelemetry-configure";
        var exported = new List<Metric>();

        // The metrics options are left at their defaults on the app (denied requests only, no tags):
        // AddIPRestrictionsInstrumentation configures them
        await using var app = CreateApp(testPort: 63, route, configureMetrics: null, configureServices: services =>
            services.AddOpenTelemetry().WithMetrics(metrics => metrics
                .AddIPRestrictionsInstrumentation(options =>
                {
                    options.TotalRequests = true;
                    options.AllowedRequests = true;
                    options.ConfigureTags = TagRoute;
                })
                .AddInMemoryExporter(exported)));
        await MakeRequests(app, route);

        app.Services.GetRequiredService<MeterProvider>().ForceFlush().Should().BeTrue();

        CountExported(exported, route).Should().BeEquivalentTo(new Dictionary<string, long>
        {
            [IPRestrictionsMetrics.RequestsInstrumentName] = 4,
            [IPRestrictionsMetrics.AllowedRequestsInstrumentName] = 2,
            [IPRestrictionsMetrics.DeniedRequestsInstrumentName] = 1,
        });
    }

    [Fact]
    public void AddIPRestrictionsInstrumentationShouldRejectNullBuilder()
    {
        var add = () => ((MeterProviderBuilder)null!).AddIPRestrictionsInstrumentation();
        add.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    /// <summary>
    /// Sums the exported IP restrictions metrics tagged with <paramref name="route"/>, by instrument.
    /// </summary>
    private static Dictionary<string, long> CountExported(List<Metric> exported, string route)
    {
        // From .NET 8 each app (service provider) has its own meter, and apps of other tests running in parallel
        // are exported as metrics of their own: count this test's route across all of them
        var metrics = exported.Where(m => m.MeterName == IPRestrictionsMetrics.MeterName).ToList();
        metrics.Should().OnlyContain(m => Instruments.Contains(m.Name) && m.Unit == "{request}");

        var counts = new Dictionary<string, long>();
        foreach (var metric in metrics)
        {
            foreach (ref readonly var point in metric.GetMetricPoints())
            {
                foreach (var tag in point.Tags)
                {
                    if (tag.Key == "http.route" && Equals(tag.Value, route))
                        counts[metric.Name] = counts.GetValueOrDefault(metric.Name) + point.GetSumLong();
                }
            }
        }
        return counts;
    }
}

/// <summary>
/// Measurements without tags can't be told apart from those of other tests, so these tests run on their own.
/// </summary>
[CollectionDefinition(nameof(MetricsWithoutTagsTests), DisableParallelization = true)]
public class MetricsWithoutTagsCollection;

[Collection(nameof(MetricsWithoutTagsTests))]
public class MetricsWithoutTagsTests
{
    [Fact]
    public async Task MeasurementsShouldHaveNoTagsWithoutConfigureTags()
    {
        const string route = "/metrics/no-tags";
        var measurements = new ConcurrentBag<(string Instrument, int TagCount)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == IPRestrictionsMetrics.MeterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            measurements.Add((instrument.Name, tags.Length)));
        listener.Start();

        // Default metrics options: denied requests, no tags
        await using var app = IPRestrictionsMetricsTests.CreateApp(testPort: 64, route, configureMetrics: null);
        await app.InvokeGet(route, IPRestrictionsMetricsTests.AllowedIP);
        await app.EnsureFailure(route, IPRestrictionsMetricsTests.DeniedIP);

        measurements.Should().BeEquivalentTo(new[] { (IPRestrictionsMetrics.DeniedRequestsInstrumentName, 0) });
    }
}
