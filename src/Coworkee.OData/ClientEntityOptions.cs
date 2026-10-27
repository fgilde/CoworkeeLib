using System.Reflection;
using Coworkee.Domain;

namespace Coworkee.OData;

/// <summary>
/// Which OData sets clients may mirror: entities marked [ClientEntity], those named with <see cref="For{TEntity}"/> and those an
/// application service names with <c>ClientEntity&lt;TEntity&gt;()</c>. Each starts from <see cref="Default"/>, then the attribute,
/// then <see cref="For{TEntity}"/>, then the service apply.
/// </summary>
public sealed class ClientEntityOptions
{
    public const string Section = "Coworkee:ClientEntities";

    private readonly Dictionary<Type, Action<ClientEntitySettings>?> _entities = [];
    private readonly Dictionary<Type, Action<ClientEntitySettings>?> _services = [];

    public ClientEntitySettings Default { get; set; } = new();

    /// <summary>Lets clients mirror <typeparamref name="TEntity"/>; <paramref name="configure"/> overrides the defaults, Enabled = false opts an attributed entity out.</summary>
    public ClientEntityOptions For<TEntity>(Action<ClientEntitySettings>? configure = null)
        where TEntity : class
    {
        _entities[typeof(TEntity)] = configure;
        return this;
    }

    internal void ForService(Type entityType, Action<ClientEntitySettings>? configure) => _services[entityType] = configure;

    internal ClientEntitySettings? Resolve(Type entityType)
    {
        var attribute = entityType.GetCustomAttribute<ClientEntityAttribute>(inherit: false);
        var named = _entities.TryGetValue(entityType, out var configure);
        if (!_services.TryGetValue(entityType, out var service) && !named && attribute is null)
        {
            return null;
        }

        var settings = Default with { };
        if (attribute is { MaxRows: > 0 })
        {
            settings.MaxRows = attribute.MaxRows;
        }

        configure?.Invoke(settings);
        service?.Invoke(settings);
        return settings.Enabled ? settings : null;
    }
}

public sealed record ClientEntitySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Rows per page of the initial download, capped by CoworkeeODataOptions.MaxTop.</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>Sets with more visible rows stay on the server: clients keep querying OData instead of downloading them.</summary>
    public int MaxRows { get; set; } = 50_000;

    /// <summary>How often clients ask for changes; entities with [Realtime] are also refreshed as soon as they change.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Date properties (DateTime or DateTimeOffset, UTC) that tell when a row was created or last changed, for entities without IAuditable,
    /// e.g. the tables of an external database. Without them such sets download completely on every refresh.
    /// </summary>
    public IReadOnlyList<string> ChangedAt { get; set; } = [];

    /// <summary>How far before the last refresh each refresh looks again, for commits that land after their timestamp; raise it for tables another system writes with its own clock.</summary>
    public TimeSpan Overlap { get; set; } = TimeSpan.FromSeconds(5);
}
