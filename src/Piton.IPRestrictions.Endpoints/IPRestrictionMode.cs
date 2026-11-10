namespace Piton.IPRestrictions.Endpoints;

/// <summary>
/// Defines the mode of operation for the IP Restrictions middleware.
/// </summary>
[Flags]
public enum IPRestrictionMode
{
    /// <summary>
    /// The middleware is disabled (including the addition of the remote endpoint header).
    /// </summary>
    Disabled = 1 << 0,
    
    /// <summary>
    /// The middleware will add a header with the name specified
    /// in <see cref="IPRestrictionsOptions.RemoteEndpointHeaderName"/>
    /// </summary>
    AddEndpointHeader = 1 << 1,
    
    /// <summary>
    /// The middleware will apply IP restrictions based on the configured options.
    /// </summary>
    Enabled = 1 << 2
}