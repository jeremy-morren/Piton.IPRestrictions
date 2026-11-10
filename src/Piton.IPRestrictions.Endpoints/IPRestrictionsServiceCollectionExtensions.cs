namespace Piton.IPRestrictions.Endpoints;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to add IP restrictions services.
/// </summary>
public static class IPRestrictionsServiceCollectionExtensions
{
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds IP restrictions middleware to the service collection.
        /// </summary>
        public IServiceCollection AddIPRestrictions(string configSection = "IPRestrictions")
        {
            services.AddIPRestrictionsCore();
            services.AddOptions<IPRestrictionsOptions>()
                .BindConfiguration(configSection);

            return services;
        }

        /// <summary>
        /// Adds IP restrictions middleware to the service collection.
        /// </summary>
        public IServiceCollection AddIPRestrictions(Action<IPRestrictionsOptions> configureOptions)
        {
            services.AddIPRestrictionsCore();
            services.Configure(configureOptions);

            return services;
        }

        /// <summary>
        /// Adds IP Restrictions options to the service collection.
        /// </summary>
        private void AddIPRestrictionsCore()
        {
            if (services.Any(s => s.ServiceType == typeof(IPRestrictionsMiddleware)))
                return;

            services.AddOptions<IPRestrictionsOptions>()
                .Validate(o => o.Validate())
                .PostConfigure<IServiceProvider>((o, sp) =>
                    o.ParseIPAddresses(sp.GetService<ILogger<IPRestrictionsOptions>>()));

            services.AddTransient<IPRestrictionsMiddleware>();

            services.AddMetrics();
            services.AddSingleton<IPRestrictionsInstrumentation>();
        }
    }

}