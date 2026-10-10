using System.Reflection;
using Coworkee.Domain;

namespace Coworkee.OData;

/// <summary>
/// Which OData sets clients may mirror: entities marked [ClientEntity] and those named with <see cref="For{TEntity}"/>,
/// each with <see cref="Default"/> and its own overrides.
/// </summary>
public sealed class ClientEntityOptions
{
    public const string Section = "Coworkee:ClientEntities";

    private readonly Dictionary<Type, Action<ClientEntitySettings>?> _entities = [];

    public ClientEntitySettings Default { get; set; } = new();

    /// <summary>Lets clients mirror <typeparamref name="TEntity"/>; <paramref name="configure"/> overrides the defaults, Enabled = false opts an attributed entity out.</summary>
    public ClientEntityOptions For<TEntity>(Action<ClientEntitySettings>? configure = null)
        where TEntity : class
    {
        _entities[typeof(TEntity)] = configure;
        return this;
    }

    internal ClientEntitySettings? Resolve(Type entityType)
    {
        var attribute = entityType.GetCustomAttribute<ClientEntityAttribute>(inherit: false);
        if (!_entities.TryGetValue(entityType, out var configure) && attribute is null)
        {
            return null;
        }

        var settings = Default with { };
        if (attribute is { MaxRows: > 0 })
        {
            settings.MaxRows = attribute.MaxRows;
        }

        configure?.Invoke(settings);
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
}
