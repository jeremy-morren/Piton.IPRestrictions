namespace AspNetCore.IPRestrictions.Endpoints;

public static class IPRestrictionsEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Applies IP restrictions to the endpoint.
    /// </summary>
    public static void RestrictIP(this EndpointBuilder endpointBuilder) 
    {
        endpointBuilder.Metadata.Add(RestrictIPMetadata);
    }
    
    /// <param name="builder">
    /// The <see cref="IEndpointConventionBuilder"/> endpoint convention builder to add IP restrictions to
    /// </param>
    /// <typeparam name="TBuilder">
    /// The type of <see cref="IEndpointConventionBuilder"/>
    /// </typeparam>
    extension<TBuilder>(TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Indicates that request IP restrictions should be applied to the endpoints
        /// </summary>
        /// <returns>The builder instance so that further calls can be chained</returns>
        public TBuilder RestrictIP()
        {
            builder.Add(b => b.RestrictIP());
            return builder;
        }

        /// <summary>
        /// Indicates that request IP restrictions should be applied to the endpoint
        /// </summary>
        /// <param name="predicate">A predicate that takes the HttpContext and returns true if IP restrictions should be applied</param>
        /// <returns>The builder instance so that further calls can be chained</returns>
        public TBuilder RestrictIP(Predicate<HttpContext> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            builder.Add(endpoint =>
            {
                endpoint.Metadata.Add(new RestrictIPAttribute(predicate));
            });
            return builder;
        }
    }
    
    private static readonly RestrictIPAttribute RestrictIPMetadata = new();
}