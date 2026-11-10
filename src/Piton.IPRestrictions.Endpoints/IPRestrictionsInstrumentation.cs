using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;

namespace Piton.IPRestrictions.Endpoints;

/// <summary>
/// Records the <see cref="IPRestrictionsMetrics"/> instruments.
/// </summary>
internal sealed class IPRestrictionsInstrumentation
{
    private static readonly string? Version = typeof(IPRestrictionsInstrumentation).Assembly.GetName().Version?.ToString();

    private readonly Counter<long> _requests;
    private readonly Counter<long> _allowedRequests;
    private readonly Counter<long> _deniedRequests;

#if NET8_0_OR_GREATER
    // .NET 8+: take IMeterFactory from DI, so that the meter is scoped to the service provider and disposed with it.
    public IPRestrictionsInstrumentation(IMeterFactory meterFactory)
        : this(meterFactory.Create(IPRestrictionsMetrics.MeterName, Version))
    {
    }
#else
    private static readonly Meter SharedMeter = new(IPRestrictionsMetrics.MeterName, Version);

    public IPRestrictionsInstrumentation()
        : this(SharedMeter)
    {
    }
#endif

    private IPRestrictionsInstrumentation(Meter meter)
    {
        _requests = meter.CreateCounter<long>(
            IPRestrictionsMetrics.RequestsInstrumentName,
            unit: "{request}",
            description: "Number of requests to endpoints with IP restrictions.");
        _allowedRequests = meter.CreateCounter<long>(
            IPRestrictionsMetrics.AllowedRequestsInstrumentName,
            unit: "{request}",
            description: "Number of allowed requests to endpoints with IP restrictions.");
        _deniedRequests = meter.CreateCounter<long>(
            IPRestrictionsMetrics.DeniedRequestsInstrumentName,
            unit: "{request}",
            description: "Number of denied requests to endpoints with IP restrictions.");
    }

    /// <summary>
    /// Records a request to an endpoint with IP restrictions.
    /// </summary>
    public void Request(IPRestrictionsMetricsOptions options, Endpoint endpoint, IPAddress? remoteIp) =>
        Record(_requests, options.TotalRequests, options, endpoint, remoteIp);

    /// <summary>
    /// Records an allowed request to an endpoint with IP restrictions.
    /// </summary>
    public void Allowed(IPRestrictionsMetricsOptions options, Endpoint endpoint, IPAddress? remoteIp) =>
        Record(_allowedRequests, options.AllowedRequests, options, endpoint, remoteIp);

    /// <summary>
    /// Records a denied request to an endpoint with IP restrictions.
    /// </summary>
    public void Denied(IPRestrictionsMetricsOptions options, Endpoint endpoint, IPAddress? remoteIp) =>
        Record(_deniedRequests, options.DeniedRequests, options, endpoint, remoteIp);

    private static void Record(Counter<long> counter, bool enabled, IPRestrictionsMetricsOptions options, Endpoint endpoint, IPAddress? remoteIp)
    {
        // Enabled: the counter has a listener (e.g. OpenTelemetry)
        if (!enabled || !counter.Enabled)
            return;

        var configureTags = options.ConfigureTags;
        if (configureTags == null)
        {
            counter.Add(1);
            return;
        }

        // An IPv4 client on a dual mode socket arrives as ::ffff:a.b.c.d:
        // pass the IPv4 address, so that one client is one address whichever way it connected
        if (remoteIp is { IsIPv4MappedToIPv6: true })
            remoteIp = remoteIp.MapToIPv4();

        var tags = new TagList();
        configureTags(ref tags, endpoint, remoteIp);
        counter.Add(1, tags);
    }
}