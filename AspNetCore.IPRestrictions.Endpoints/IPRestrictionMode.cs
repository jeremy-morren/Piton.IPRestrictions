namespace AspNetCore.IPRestrictions.Endpoints;

/// <summary>
/// Defines the mode of operation for the IP Restrictions middleware.
/// </summary>
public enum IPRestrictionMode
{
    /// <summary>
    /// The middleware is disabled
    /// </summary>
    Disabled = 1,
    
    /// <summary>
    /// The middleware will add a header with the name specified in <see cref="IPRestrictionsOptions.RemoteEndpointHeaderName"/>
    /// but not apply any IP restrictions.
    /// </summary>
    AddEndpointHeader = 2,
    
    /// <summary>
    /// The middleware will apply IP restrictions based on the configured options.
    /// </summary>
    Enabled = 3
}