using Coworkee.Contracts.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Settings;

public static class AppConfigurationRegistrationExtensions
{
    private static readonly AppConfigurationRules<CoworkeeAppSettings> AppSettingsRules = new AppConfigurationRules<CoworkeeAppSettings>()
        .Protect(s => s.Jobs.ConnectionStringName, s => s.Jobs.RunServer, s => s.Jobs.DashboardPath, s => s.Notifications.PublicAppUrl)
        // the configuration binder appends to collections that start with items, so these could never be edited
        .Hide(s => s.Jobs.Queues, s => s.Jobs.RetryDelaysInSeconds)
        // the VAPID keys come with the deployment; another pair would silence every subscribed device
        .Hide(s => s.Notifications.Push);

    /// <summary>Binds <typeparamref name="T"/> to the section (IOptions/IOptionsMonitor) and lets admins of the system organisation edit it under Settings.</summary>
    public static IServiceCollection AddCoworkeeAppConfiguration<T>(this IServiceCollection services, IConfiguration configuration, string section, string title,
        Action<AppConfigurationRules<T>>? rules = null)
        where T : class
    {
        services.Configure<T>(configuration.GetSection(section));
        services.AddSingleton(Registration(section, title, rules));
        return services;
    }

    /// <summary>
    /// The typed app settings, replacing the default <see cref="CoworkeeAppSettings"/>. A type derived from it keeps the locks of the built-in sections;
    /// any other type replaces them completely and usually brings its own <paramref name="section"/>.
    /// </summary>
    public static IServiceCollection AddCoworkeeSettings<T>(this IServiceCollection services, IConfiguration configuration, Action<AppConfigurationRules<T>>? rules = null,
        string section = CoworkeeAppSettings.Section, string title = "App settings")
        where T : class
    {
        foreach (var existing in services.Where(IsAppSettings).ToList())
        {
            services.Remove(existing);
        }

        var registration = Registration(section, title, rules);
        if (typeof(CoworkeeAppSettings).IsAssignableFrom(typeof(T)))
        {
            registration = registration with
            {
                Locked = [.. AppSettingsRules.Locked, .. registration.Locked],
                Protected = [.. AppSettingsRules.Protected, .. registration.Protected],
                Hidden = [.. AppSettingsRules.Hidden, .. registration.Hidden],
            };
        }

        services.Configure<T>(configuration.GetSection(section));
        services.AddSingleton(registration with { IsAppSettings = true });
        return services;
    }

    internal static bool HasAppSettings(this IServiceCollection services) => services.Any(IsAppSettings);

    private static bool IsAppSettings(ServiceDescriptor descriptor) => descriptor.ImplementationInstance is AppConfigurationRegistration { IsAppSettings: true };

    private static AppConfigurationRegistration Registration<T>(string section, string title, Action<AppConfigurationRules<T>>? configure)
    {
        var rules = new AppConfigurationRules<T>();
        configure?.Invoke(rules);
        return new AppConfigurationRegistration(section, title, typeof(T)) { Locked = rules.Locked, Protected = rules.Protected, Hidden = rules.Hidden };
    }
}
