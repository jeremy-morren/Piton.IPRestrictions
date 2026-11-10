namespace AspNetCore.IPRestrictions.Endpoints;

/// <summary>
/// Which metrics the IP restrictions middleware records. See <see cref="IPRestrictionsMetrics"/> for the instruments.
/// </summary>
/// <remarks>
/// The options are read for each request, so a configuration reload enables or disables metrics without a restart.
/// </remarks>
public class IPRestrictionsMetricsOptions
{
    /// <summary>
    /// Whether to count every request to an endpoint with IP restrictions,
    /// including requests the endpoint's predicate exempted. Default is <see langword="false" />.
    /// </summary>
    public bool TotalRequests { get; set; }

    /// <summary>
    /// Whether to count allowed requests to endpoints with IP restrictions.
    /// Default is <see langword="false" />.
    /// </summary>
    public bool AllowedRequests { get; set; }

    /// <summary>
    /// Whether to count denied requests to endpoints with IP restrictions.
    /// Default is <see langword="true" />.
    /// </summary>
    public bool DeniedRequests { get; set; } = true;

    /// <summary>
    /// Adds tags to each measurement, from the endpoint and the client's IP address.
    /// Default is <see langword="null" />: measurements have no tags.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For example, to tag measurements with the route (<c>http.route</c>) and the client's IP address
    /// (<c>client.address</c>, the OpenTelemetry semantic conventions):
    /// </para>
    /// <code>
    /// options.Metrics.ConfigureTags = (ref TagList tags, Endpoint endpoint, IPAddress? remoteIp) =>
    /// {
    ///     if (endpoint is RouteEndpoint { RoutePattern.RawText: { } route })
    ///         tags.Add("http.route", route);
    ///     if (remoteIp != null)
    ///         tags.Add("client.address", remoteIp.ToString());
    /// };
    /// </code>
    /// <para>
    /// Each distinct combination of tag values is a separate time series. Tagging with the client's IP address
    /// makes their number unbounded: the memory and storage of the metrics grow with the number of clients
    /// (for denied requests, with the number of addresses scanning the application).
    /// </para>
    /// <para>
    /// Not bound from configuration.
    /// </para>
    /// </remarks>
    public IPRestrictionsMetricTagsCallback? ConfigureTags { get; set; }
}
