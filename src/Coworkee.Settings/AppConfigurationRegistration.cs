using System.Linq.Expressions;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Settings;

/// <summary>
/// A typed section admins may edit; the type binds the section (usually generated from a JSON file with Nextended.CodeGen).
/// <see cref="Locked"/> and <see cref="Hidden"/> are property paths below the section; locked values show but never change, hidden ones never leave the server.
/// </summary>
public sealed record AppConfigurationRegistration(string Section, string Title, Type Type)
{
    public IReadOnlyList<string> Locked { get; init; } = [];

    public IReadOnlyList<string> Hidden { get; init; } = [];

    /// <summary>The app settings (one per app, see AddCoworkeeSettings); the other sections come next to it.</summary>
    public bool IsAppSettings { get; init; }
}

/// <summary>Which properties of <typeparamref name="T"/> admins may not change (<see cref="Lock"/>) or not even see (<see cref="Hide"/>).</summary>
public sealed class AppConfigurationRules<T>
{
    internal List<string> Locked { get; } = [];

    internal List<string> Hidden { get; } = [];

    public AppConfigurationRules<T> Lock(params Expression<Func<T, object?>>[] properties)
    {
        Locked.AddRange(properties.Select(PathOf));
        return this;
    }

    public AppConfigurationRules<T> Hide(params Expression<Func<T, object?>>[] properties)
    {
        Hidden.AddRange(properties.Select(PathOf));
        return this;
    }

    private static string PathOf(Expression<Func<T, object?>> property)
    {
        var names = new Stack<string>();
        var node = property.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : property.Body;
        while (node is MemberExpression member)
        {
            names.Push(member.Member.Name);
            node = member.Expression;
        }

        return node is ParameterExpression && names.Count > 0 ? string.Join(':', names) : throw new ArgumentException($"{property} is not a property path.", nameof(property));
    }
}

public static class AppConfigurationRegistrationExtensions
{
    private static readonly AppConfigurationRules<CoworkeeAppSettings> AppSettingsRules = new AppConfigurationRules<CoworkeeAppSettings>()
        .Lock(s => s.Jobs.ConnectionStringName, s => s.Jobs.RunServer, s => s.Jobs.DashboardPath, s => s.Notifications.PublicAppUrl)
        // the configuration binder appends to collections that start with items, so these could never be edited
        .Hide(s => s.Jobs.Queues, s => s.Jobs.RetryDelaysInSeconds);

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
                Locked = [.. AppSettingsRules.Locked, .. registration.Locked], Hidden = [.. AppSettingsRules.Hidden, .. registration.Hidden],
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
        return new AppConfigurationRegistration(section, title, typeof(T)) { Locked = rules.Locked, Hidden = rules.Hidden };
    }
}
