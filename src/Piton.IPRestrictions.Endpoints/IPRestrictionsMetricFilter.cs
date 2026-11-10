using System.Net;

namespace Piton.IPRestrictions.Endpoints;

/// <summary>
/// Whether to record an IP restrictions metric measurement. See <see cref="IPRestrictionsMetricsOptions.Filter"/>.
/// </summary>
/// <param name="endpoint">The endpoint the request was routed to.</param>
/// <param name="remoteIp">
/// The client's IP address, or <see langword="null"/> when it is unknown.
/// An IPv4-mapped IPv6 address (<c>::ffff:a.b.c.d</c>, an IPv4 client on a dual mode socket) is given as the IPv4 address.
/// </param>
/// <param name="denied">
/// Whether the request was denied. <see langword="false"/> for allowed requests,
/// and for requests the endpoint's predicate exempted from the IP restrictions.
/// </param>
/// <returns><see langword="true"/> to record the measurement, <see langword="false"/> to leave it out.</returns>
public delegate bool IPRestrictionsMetricFilter(Endpoint endpoint, IPAddress? remoteIp, bool denied);
