using System.Reflection;
using Coworkee.Contracts.Data;
using Coworkee.Domain;

namespace Coworkee.OData;

/// <summary>
/// Which OData sets clients may mirror: entities marked [ClientEntity], those named with <see cref="For{TEntity}"/> and those an
/// application service names with <c>ClientEntity&lt;TEntity&gt;()</c>. Each starts from <see cref="Default"/>, then the attribute,
/// then <see cref="For{TEntity}"/>, then the service apply; last the override of the set in <see cref="Sets"/> (configuration, the Local copies page).
/// </summary>
public sealed class ClientEntityOptions
{
    public const string Section = "Coworkee:ClientEntities";

    private readonly Dictionary<Type, Action<ClientEntitySettings>?> _entities = [];
    private readonly Dictionary<Type, Action<ClientEntitySettings>?> _services = [];

    public ClientEntitySettings Default { get; set; } = new();

    /// <summary>
    /// How long rows reported through <see cref="ClientEntities.IClientEntityChangeLog"/> are kept; a copy last refreshed longer ago than this
    /// is downloaded again, since touches it would need are gone.
    /// </summary>
    public TimeSpan ChangeLogLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Overrides per entity set (Coworkee:ClientEntities:Sets:{EntitySet}), applied after the code: they may also mirror a set the code did not mark
    /// (Copy Memory or Sqlite) or keep a marked one on the server (Copy Off). The Local copies page saves them in the app's configuration.
    /// </summary>
    public Dictionary<string, ClientEntitySetOverride> Sets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lets clients mirror <typeparamref name="TEntity"/>; <paramref name="configure"/> overrides the defaults, Enabled = false opts an attributed entity out.</summary>
    public ClientEntityOptions For<TEntity>(Action<ClientEntitySettings>? configure = null)
        where TEntity : class
    {
        _entities[typeof(TEntity)] = configure;
        return this;
    }

    internal void ForService(Type entityType, Action<ClientEntitySettings>? configure) => _services[entityType] = configure;

    /// <summary>The settings of the set, or null when clients do not mirror it; <paramref name="entitySet"/> names the set whose override applies.</summary>
    internal ClientEntitySettings? Resolve(Type entityType, string? entitySet = null) => Build(entityType, entitySet, null) is { Enabled: true } settings ? settings : null;

    /// <summary>The settings of the set even when it is not mirrored (Enabled false), and what decided each value (keys as in <see cref="ClientEntityAdminSetDto.Sources"/>).</summary>
    internal (ClientEntitySettings Settings, Dictionary<string, ClientEntitySettingSource> Sources) Describe(Type entityType, string entitySet)
    {
        var sources = Tracked.ToDictionary(t => t.Name, _ => ClientEntitySettingSource.Default);
        return (Build(entityType, entitySet, sources)!, sources);
    }

    // the values the Local copies page shows; Copy stands for Enabled and Copy together
    private static readonly (string Name, Func<ClientEntitySettings, object?> Get)[] Tracked =
    [
        (nameof(ClientEntitySettings.Copy), s => s.Enabled ? s.Copy is ClientEntityCopy.Off ? null : s.Copy : ClientEntityCopy.Off),
        (nameof(ClientEntitySettings.MaxRows), s => s.MaxRows),
        (nameof(ClientEntitySettings.BatchSize), s => s.BatchSize),
        (nameof(ClientEntitySettings.ParallelPages), s => s.ParallelPages),
        (nameof(ClientEntitySettings.RefreshInterval), s => s.RefreshInterval),
        (nameof(ClientEntitySettings.DefaultMode), s => s.DefaultMode),
    ];

    private ClientEntitySettings? Build(Type entityType, string? entitySet, Dictionary<string, ClientEntitySettingSource>? sources)
    {
        var attribute = entityType.GetCustomAttribute<ClientEntityAttribute>(inherit: false);
        var named = _entities.TryGetValue(entityType, out var configure);
        var marked = _services.TryGetValue(entityType, out var service) | named | attribute is not null;
        var set = entitySet is not null && Sets.Count > 0 ? Sets.GetValueOrDefault(entitySet) : null;
        if (!marked && sources is null && set?.Copy is not (ClientEntityCopy.Memory or ClientEntityCopy.Sqlite))
        {
            return null;
        }

        var settings = Default with { };
        settings.Enabled &= marked;
        if (sources is null)
        {
            // the feed asks for every request: the steps without the bookkeeping, no delegate allocated
            FromAttribute(settings, attribute);
            configure?.Invoke(settings);
            service?.Invoke(settings);
            set?.ApplyTo(settings);
        }
        else
        {
            if (marked)
            {
                // the first that names the set mirrors it
                sources[nameof(ClientEntitySettings.Copy)] = attribute is not null ? ClientEntitySettingSource.Attribute
                    : named ? ClientEntitySettingSource.Code
                    : ClientEntitySettingSource.Service;
            }

            Apply(settings, sources, ClientEntitySettingSource.Attribute, attribute is null ? null : s => FromAttribute(s, attribute));
            Apply(settings, sources, ClientEntitySettingSource.Code, configure);
            Apply(settings, sources, ClientEntitySettingSource.Service, service);
            Apply(settings, sources, ClientEntitySettingSource.Override, set is null ? null : set.ApplyTo);
        }

        if (settings.Copy is ClientEntityCopy.Off)
        {
            (settings.Enabled, settings.Copy) = (false, null);
        }

        return settings;
    }

    private static void FromAttribute(ClientEntitySettings settings, ClientEntityAttribute? attribute)
    {
        if (attribute is { MaxRows: > 0 })
        {
            settings.MaxRows = attribute.MaxRows;
        }

        if (attribute is { Expand: { Length: > 0 } expand })
        {
            settings.Expand = expand;
        }
    }

    // each value a step changes is put down to that step
    private static void Apply(ClientEntitySettings settings, Dictionary<string, ClientEntitySettingSource> sources, ClientEntitySettingSource source, Action<ClientEntitySettings>? step)
    {
        if (step is null)
        {
            return;
        }

        var before = Tracked.Select(t => t.Get(settings)).ToList();
        step(settings);
        for (var i = 0; i < Tracked.Length; i++)
        {
            if (!Equals(before[i], Tracked[i].Get(settings)))
            {
                sources[Tracked[i].Name] = source;
            }
        }
    }
}

internal static class ClientEntitySetOverrideExtensions
{
    public static void ApplyTo(this ClientEntitySetOverride set, ClientEntitySettings settings)
    {
        if (set.Copy is { } copy)
        {
            (settings.Enabled, settings.Copy) = (copy is not ClientEntityCopy.Off, copy);
        }

        if (set.MaxRows is int maxRows and > 0)
        {
            settings.MaxRows = maxRows;
        }

        if (set.BatchSize is int batch and > 0)
        {
            settings.BatchSize = batch;
        }

        if (set.ParallelPages is int parallel and > 0)
        {
            settings.ParallelPages = parallel;
        }

        settings.RefreshInterval = set.RefreshInterval ?? settings.RefreshInterval;
        settings.DefaultMode = set.DefaultMode ?? settings.DefaultMode;
    }
}
