using System.Diagnostics;
using System.Net;

namespace Piton.IPRestrictions.Endpoints;

/// <summary>
/// Adds tags to an IP restrictions metric measurement. See <see cref="IPRestrictionsMetricsOptions.ConfigureTags"/>.
/// </summary>
/// <param name="tags">
/// The measurement's tags, empty when the callback is invoked. Passed by reference, since <see cref="TagList"/> is a struct.
/// </param>
/// <param name="endpoint">The endpoint the request was routed to.</param>
/// <param name="remoteIp">
/// The client's IP address, or <see langword="null"/> when it is unknown.
/// An IPv4-mapped IPv6 address (<c>::ffff:a.b.c.d</c>, an IPv4 client on a dual mode socket) is given as the IPv4 address.
/// </param>
/// <param name="denied">
/// Whether the request was denied. <see langword="false"/> for allowed requests,
/// and for requests the endpoint's predicate exempted from the IP restrictions.
/// </param>
public delegate void IPRestrictionsMetricTagsCallback(ref TagList tags, Endpoint endpoint, IPAddress? remoteIp, bool denied);
