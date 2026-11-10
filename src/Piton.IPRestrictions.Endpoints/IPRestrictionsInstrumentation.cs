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

    // Take IMeterFactory from DI, so that the meter is scoped to the service provider and disposed with it.
    public IPRestrictionsInstrumentation(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(IPRestrictionsMetrics.MeterName, Version);

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
    public void Request(IPRestrictionsMetricsOptions options, Endpoint endpoint, IPAddress? remoteIp, bool denied) =>
        Record(_requests, options.TotalRequests, options, endpoint, remoteIp, denied);

    /// <summary>
    /// Records an allowed request to an endpoint with IP restrictions.
    /// </summary>
    public void Allowed(IPRestrictionsMetricsOptions options, Endpoint endpoint, IPAddress? remoteIp) =>
        Record(_allowedRequests, options.AllowedRequests, options, endpoint, remoteIp, denied: false);

    /// <summary>
    /// Records a denied request to an endpoint with IP restrictions.
    /// </summary>
    public void Denied(IPRestrictionsMetricsOptions options, Endpoint endpoint, IPAddress? remoteIp) =>
        Record(_deniedRequests, options.DeniedRequests, options, endpoint, remoteIp, denied: true);

    private static void Record(Counter<long> counter, bool enabled, IPRestrictionsMetricsOptions options, Endpoint endpoint, IPAddress? remoteIp, bool denied)
    {
        // Enabled: the counter has a listener (e.g. OpenTelemetry)
        if (!enabled || !counter.Enabled)
            return;

        var (filter, configureTags) = (options.Filter, options.ConfigureTags);

        // An IPv4 client on a dual mode socket arrives as ::ffff:a.b.c.d: give the callbacks the IPv4 address,
        // so that one client is one address whichever way it connected
        if ((filter != null || configureTags != null) && remoteIp is { IsIPv4MappedToIPv6: true })
            remoteIp = remoteIp.MapToIPv4();

        if (filter != null && !filter(endpoint, remoteIp, denied))
            return;

        if (configureTags == null)
        {
            counter.Add(1);
            return;
        }

        var tags = new TagList();
        configureTags(ref tags, endpoint, remoteIp, denied);
        counter.Add(1, tags);
    }
}