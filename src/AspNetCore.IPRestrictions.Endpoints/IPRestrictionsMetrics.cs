namespace AspNetCore.IPRestrictions.Endpoints;

/// <summary>
/// Metrics emitted by the IP restrictions middleware, using <see cref="System.Diagnostics.Metrics"/>.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="MeterName"/> meter has three counters of requests to endpoints with IP restrictions,
/// each enabled by <see cref="IPRestrictionsOptions.Metrics"/>:
/// </para>
/// <list type="bullet">
/// <item><see cref="RequestsInstrumentName"/>: every request, including requests the endpoint's predicate exempted
/// (<see cref="IPRestrictionsMetricsOptions.TotalRequests"/>, off by default)</item>
/// <item><see cref="AllowedRequestsInstrumentName"/>: allowed requests
/// (<see cref="IPRestrictionsMetricsOptions.AllowedRequests"/>, off by default)</item>
/// <item><see cref="DeniedRequestsInstrumentName"/>: denied requests
/// (<see cref="IPRestrictionsMetricsOptions.DeniedRequests"/>, on by default)</item>
/// </list>
/// <para>
/// Measurements have no tags, unless <see cref="IPRestrictionsMetricsOptions.ConfigureTags"/> adds them.
/// </para>
/// <para>
/// With OpenTelemetry, add the meter with the <c>AspNetCore.IPRestrictions.Endpoints.Telemetry</c> package,
/// or with <c>AddMeter(IPRestrictionsMetrics.MeterName)</c>.
/// </para>
/// </remarks>
public static class IPRestrictionsMetrics
{
    /// <summary>
    /// The name of the meter.
    /// </summary>
    public const string MeterName = "AspNetCore.IPRestrictions";

    /// <summary>
    /// The name of the counter of all requests to endpoints with IP restrictions.
    /// </summary>
    public const string RequestsInstrumentName = "aspnetcore.ip_restrictions.requests";

    /// <summary>
    /// The name of the counter of allowed requests to endpoints with IP restrictions.
    /// </summary>
    public const string AllowedRequestsInstrumentName = "aspnetcore.ip_restrictions.allowed_requests";

    /// <summary>
    /// The name of the counter of denied requests to endpoints with IP restrictions.
    /// </summary>
    public const string DeniedRequestsInstrumentName = "aspnetcore.ip_restrictions.denied_requests";
}