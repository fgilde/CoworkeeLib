using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

/// <summary>Whether and where clients keep the copy of a set: not at all (server only), as columns in memory (kept in IndexedDB) or in SQLite in the browser.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClientEntityCopy>))]
public enum ClientEntityCopy
{
    Off,
    Memory,
    Sqlite,
}

/// <summary>Where the queries of a mirrored set run for users who did not choose themselves.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClientEntityDefaultMode>))]
public enum ClientEntityDefaultMode
{
    Client,
    Server,
}

/// <summary>What decided a value of a set's client entity settings.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClientEntitySettingSource>))]
public enum ClientEntitySettingSource
{
    /// <summary>The defaults (Coworkee:ClientEntities:Default); for the copy: the code does not mark the set.</summary>
    Default,

    /// <summary>[ClientEntity] on the entity.</summary>
    Attribute,

    /// <summary>AddCoworkeeClientEntities(o =&gt; o.For&lt;T&gt;(...)).</summary>
    Code,

    /// <summary>ClientEntity&lt;T&gt;() on an application service.</summary>
    Service,

    /// <summary>Coworkee:ClientEntities:Sets:{EntitySet} in the configuration, usually saved on the Local copies page.</summary>
    Override,
}

/// <summary>
/// What an administrator changed of one set (configuration Coworkee:ClientEntities:Sets:{EntitySet}); every value left null keeps what the code says.
/// <see cref="Copy"/> Memory or Sqlite also mirrors a set the code did not mark, Off keeps a marked one on the server.
/// </summary>
public sealed class ClientEntitySetOverride
{
    public const string Section = "Coworkee:ClientEntities:Sets";

    public ClientEntityCopy? Copy { get; set; }

    public int? MaxRows { get; set; }

    public int? BatchSize { get; set; }

    public int? ParallelPages { get; set; }

    public TimeSpan? RefreshInterval { get; set; }

    public ClientEntityDefaultMode? DefaultMode { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Copy is null && MaxRows is null && BatchSize is null && ParallelPages is null && RefreshInterval is null && DefaultMode is null;
}

/// <summary>
/// A set that could be mirrored (an OData set with a Guid Id), its effective settings and what decided each of them (<see cref="Sources"/>, keyed by the
/// property name: Copy, MaxRows, BatchSize, ParallelPages, RefreshInterval, DefaultMode).
/// </summary>
/// <param name="Copy">Off, Memory or Sqlite; null: mirrored, the client app picks the engine (memory, or SQLite where it registered it).</param>
/// <param name="Override">What the configuration changes of the set, null when nothing.</param>
public sealed record ClientEntityAdminSetDto(
    string EntitySet, string EntityType, ClientEntityCopy? Copy, int MaxRows, int BatchSize, int ParallelPages, TimeSpan RefreshInterval,
    ClientEntityDefaultMode DefaultMode, IReadOnlyDictionary<string, ClientEntitySettingSource> Sources, ClientEntitySetOverride? Override);

public static class ClientEntityTopics
{
    /// <summary>The client entity settings changed (an override saved): open clients load the manifest again.</summary>
    public const string Changed = "global:client-entities";
}
