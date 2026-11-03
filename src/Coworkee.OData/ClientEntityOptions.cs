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

    /// <summary>
    /// How long rows reported through <see cref="ClientEntities.IClientEntityChangeLog"/> are kept; a copy last refreshed longer ago than this
    /// is downloaded again, since touches it would need are gone.
    /// </summary>
    public TimeSpan ChangeLogLifetime { get; set; } = TimeSpan.FromDays(30);

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

        if (attribute is { Expand: { Length: > 0 } expand })
        {
            settings.Expand = expand;
        }

        configure?.Invoke(settings);
        service?.Invoke(settings);
        return settings.Enabled ? settings : null;
    }
}
