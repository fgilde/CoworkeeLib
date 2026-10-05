using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Coworkee.Settings;

/// <summary>One configuration value changed in the app; it overrides appsettings and environment for every service of the app.</summary>
public sealed class ConfigurationEntry
{
    public required string Key { get; set; }

    public string? Value { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}

internal sealed class AppConfigurationModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ConfigurationEntry>(entry =>
        {
            entry.ToTable("ConfigurationEntries", "cw");
            entry.HasKey(e => e.Key);
            entry.Property(e => e.Key).HasMaxLength(512);
            entry.Property(e => e.Value).IsSensitive();
        });
}

/// <summary>A typed section admins may edit; the type binds the section (usually generated from a JSON file with Nextended.CodeGen).</summary>
public sealed record AppConfigurationRegistration(string Section, string Title, Type Type);

public static class AppConfigurationExtensions
{
    /// <summary>
    /// Adds the values changed in the app (table cw.ConfigurationEntries) on top of everything configured so far. Every service of
    /// the app adds it with the same connection string; changes reach the others within <paramref name="reloadInterval"/>.
    /// </summary>
    public static IConfigurationBuilder AddCoworkeeDatabaseConfiguration(this IConfigurationBuilder builder, string connectionStringName, TimeSpan? reloadInterval = null) =>
        builder.Add(new DatabaseConfigurationSource(connectionStringName, reloadInterval ?? TimeSpan.FromSeconds(15)));

    /// <summary>
    /// The defaults of a section from a JSON document holding just the section (the file a typed class is generated from).
    /// They come first, so appsettings, environment and the values changed in the app all override them.
    /// </summary>
    public static IConfigurationBuilder AddCoworkeeAppConfigurationDefaults(this IConfigurationBuilder builder, Stream json, string section)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var source = new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource { InitialData = JsonFlattening.Flatten(section, document.RootElement) };
        builder.Sources.Insert(0, source); // a ConfigurationManager rebuilds its providers when its sources change
        return builder;
    }

    /// <summary>Binds <typeparamref name="T"/> to the section (IOptions/IOptionsMonitor) and lets admins edit it under Configuration.</summary>
    public static IServiceCollection AddCoworkeeAppConfiguration<T>(this IServiceCollection services, IConfiguration configuration, string section, string title)
        where T : class
    {
        services.Configure<T>(configuration.GetSection(section));
        services.AddSingleton(new AppConfigurationRegistration(section, title, typeof(T)));
        return services;
    }

    internal static DatabaseConfigurationProvider? DatabaseProvider(this IConfiguration configuration) =>
        (configuration as IConfigurationRoot)?.Providers.OfType<DatabaseConfigurationProvider>().FirstOrDefault();
}

public sealed class DatabaseConfigurationSource(string connectionStringName, TimeSpan reloadInterval) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        // the connection string comes from the other sources (appsettings, environment, Aspire, test hosts that add theirs later)
        if (builder is IConfiguration configured)
        {
            return new DatabaseConfigurationProvider(() => configured.GetConnectionString(connectionStringName), reloadInterval);
        }

        var connectionString = new ConfigurationBuilder().AddRange(builder.Sources.Where(s => s != this)).Build().GetConnectionString(connectionStringName);
        return new DatabaseConfigurationProvider(() => connectionString, reloadInterval);
    }
}

file static class ConfigurationBuilderRange
{
    public static IConfigurationBuilder AddRange(this IConfigurationBuilder builder, IEnumerable<IConfigurationSource> sources)
    {
        foreach (var source in sources)
        {
            builder.Add(source);
        }

        return builder;
    }
}

public sealed class DatabaseConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly Func<string?> _connectionString;
    private readonly Timer _timer;

    internal DatabaseConfigurationProvider(Func<string?> connectionString, TimeSpan reloadInterval)
    {
        _connectionString = connectionString;
        _timer = new Timer(_ => Refresh(), null, reloadInterval, reloadInterval);
    }

    public override void Load() => Data = Read() ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads the entries again and raises the reload token when they changed (IOptionsMonitor follows).</summary>
    public void Refresh()
    {
        if (Read() is not { } current || (current.Count == Data.Count && current.All(e => Data.TryGetValue(e.Key, out var v) && v == e.Value)))
        {
            return;
        }

        Data = current;
        OnReload();
    }

    private Dictionary<string, string?>? Read()
    {
        string? connectionString;
        try
        {
            connectionString = _connectionString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        if (string.IsNullOrEmpty(connectionString))
        {
            return null;
        }

        try
        {
            // never let a slow database hold up the start of a service
            using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 5, CommandTimeout = 5 }.ConnectionString);
            connection.Open();
            using var command = new NpgsqlCommand("""select "Key", "Value" from cw."ConfigurationEntries" """, connection);
            using var reader = command.ExecuteReader();
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                values[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
            }

            return values;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or TimeoutException or ArgumentException)
        {
            // not migrated yet, or the database is away for a moment: keep what we have
            return null;
        }
    }

    public void Dispose() => _timer.Dispose();
}

[RequiresPermission(SettingsPermissions.Manage)]
public sealed record GetAppConfigurations : IQuery<Result<IReadOnlyList<AppConfigurationDto>>>;

[RequiresPermission(SettingsPermissions.Manage)]
public sealed record GetAppConfiguration(string Section) : IQuery<Result<AppConfigurationValuesDto>>;

[RequiresPermission(SettingsPermissions.Manage)]
public sealed record SaveAppConfiguration(string Section, JsonElement Values) : ICommand<Result<AppConfigurationValuesDto>>;

[RequiresPermission(SettingsPermissions.Manage)]
public sealed record ResetAppConfiguration(string Section) : ICommand<Result<AppConfigurationValuesDto>>;

internal sealed partial class AppConfigurationHandlers(
    IConfiguration configuration, IEnumerable<AppConfigurationRegistration> registrations, CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock, IServiceProvider services)
    : IHandler<GetAppConfigurations, Result<IReadOnlyList<AppConfigurationDto>>>,
      IHandler<GetAppConfiguration, Result<AppConfigurationValuesDto>>,
      IHandler<SaveAppConfiguration, Result<AppConfigurationValuesDto>>,
      IHandler<ResetAppConfiguration, Result<AppConfigurationValuesDto>>
{
    private static readonly Error NotFound = Error.NotFound("configuration.not_found", "There is no such configuration section.");

    private static readonly Error SystemOnly =
        Error.Forbidden("configuration.system_only", "The app configuration applies to every organisation; it is managed from the system organisation.");

    public async Task<Result<IReadOnlyList<AppConfigurationDto>>> HandleAsync(GetAppConfigurations query, CancellationToken cancellationToken) =>
        await IsSystemAsync(cancellationToken)
            ? Result<IReadOnlyList<AppConfigurationDto>>.Success(registrations.Select(r => new AppConfigurationDto(r.Section, r.Title)).ToList())
            : SystemOnly;

    public async Task<Result<AppConfigurationValuesDto>> HandleAsync(GetAppConfiguration query, CancellationToken cancellationToken) =>
        !await IsSystemAsync(cancellationToken) ? SystemOnly
        : Find(query.Section) is { } registration ? Describe(registration) : NotFound;

    public async Task<Result<AppConfigurationValuesDto>> HandleAsync(SaveAppConfiguration command, CancellationToken cancellationToken)
    {
        if (!await IsSystemAsync(cancellationToken))
        {
            return SystemOnly;
        }

        if (Find(command.Section) is not { } registration)
        {
            return NotFound;
        }

        object? typed;
        try
        {
            typed = command.Values.Deserialize(registration.Type, JsonSerializerOptions.Web);
        }
        catch (JsonException exception)
        {
            return Error.Validation("Values", $"The values do not fit the section: {exception.Message}");
        }

        if (typed is null)
        {
            return Error.Validation("Values", "The values are missing.");
        }

        // what the section would be without changes made in the app; only what differs is stored
        var current = JsonFlattening.Flatten(registration.Section, JsonSerializer.SerializeToElement(Current(registration), registration.Type));
        var defaults = JsonFlattening.Flatten(registration.Section, JsonSerializer.SerializeToElement(Defaults(registration), registration.Type));
        var wanted = JsonFlattening.Flatten(registration.Section, JsonSerializer.SerializeToElement(typed, registration.Type));
        var entries = new List<ConfigurationEntry>();
        foreach (var (key, value) in wanted)
        {
            var kept = value == AppConfigurationMask.Value && IsSecret(key) ? current.GetValueOrDefault(key) : value;
            if (!Same(kept, defaults.GetValueOrDefault(key)))
            {
                entries.Add(Entry(key, kept));
            }
        }

        // a shorter list must not keep the default's further items
        entries.AddRange(defaults.Where(d => d.Value is not null && !wanted.ContainsKey(d.Key)).Select(d => Entry(d.Key, null)));

        await ReplaceAsync(registration.Section, entries, cancellationToken);
        return Describe(registration);
    }

    public async Task<Result<AppConfigurationValuesDto>> HandleAsync(ResetAppConfiguration command, CancellationToken cancellationToken)
    {
        if (!await IsSystemAsync(cancellationToken))
        {
            return SystemOnly;
        }

        if (Find(command.Section) is not { } registration)
        {
            return NotFound;
        }

        await ReplaceAsync(registration.Section, [], cancellationToken);
        return Describe(registration);
    }

    private async Task<bool> IsSystemAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId
        && services.GetService<ITenantDirectory>() is { } tenants
        && await tenants.IsSystemTenantAsync(tenantId, cancellationToken);

    private ConfigurationEntry Entry(string key, string? value) =>
        new() { Key = key, Value = value, ModifiedAt = clock.GetUtcNow(), ModifiedBy = currentUser.UserId };

    private AppConfigurationRegistration? Find(string section) =>
        registrations.FirstOrDefault(r => string.Equals(r.Section, section, StringComparison.OrdinalIgnoreCase));

    private async Task ReplaceAsync(string section, List<ConfigurationEntry> entries, CancellationToken cancellationToken)
    {
        var prefix = section + ":";
        db.RemoveRange(await db.Set<ConfigurationEntry>().Where(e => e.Key.StartsWith(prefix)).ToListAsync(cancellationToken));
        db.AddRange(entries);
        await db.SaveChangesAsync(cancellationToken);

        // this service sees the change at once, the others with their next reload
        configuration.DatabaseProvider()?.Refresh();
    }

    private AppConfigurationValuesDto Describe(AppConfigurationRegistration registration)
    {
        var current = JsonSerializer.SerializeToNode(Current(registration), registration.Type, JsonSerializerOptions.Web);
        var defaults = JsonSerializer.SerializeToNode(Defaults(registration), registration.Type, JsonSerializerOptions.Web);
        Mask(current);
        Mask(defaults);
        var changed = configuration.DatabaseProvider() is { } provider
            ? configuration.GetSection(registration.Section).AsEnumerable().Select(p => p.Key).Where(k => provider.TryGet(k, out _)).Order().ToList()
            : [];
        return new AppConfigurationValuesDto(registration.Section, JsonSerializer.SerializeToElement(current), JsonSerializer.SerializeToElement(defaults), changed);
    }

    private object Current(AppConfigurationRegistration registration) =>
        configuration.GetSection(registration.Section).Get(registration.Type) ?? Activator.CreateInstance(registration.Type)!;

    private object Defaults(AppConfigurationRegistration registration)
    {
        // the value of every key of the section from all sources but the database one, latest source first
        var providers = (configuration as IConfigurationRoot)?.Providers.Where(p => p is not DatabaseConfigurationProvider).Reverse().ToList() ?? [];
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in configuration.GetSection(registration.Section).AsEnumerable().Select(p => p.Key))
        {
            foreach (var provider in providers)
            {
                if (provider.TryGet(key, out var value))
                {
                    values[key] = value;
                    break;
                }
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection(registration.Section).Get(registration.Type)
            ?? Activator.CreateInstance(registration.Type)!;
    }

    private static bool Same(string? a, string? b) =>
        string.Equals(a, b, StringComparison.Ordinal)
        || (bool.TryParse(a, out var x) && bool.TryParse(b, out var y) && x == y)
        || (decimal.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) && decimal.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && m == n)
        || (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b));

    private static bool IsSecret(string key) => SecretName().IsMatch(key[(key.LastIndexOf(':') + 1)..]);

    private static void Mask(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            if (node is JsonArray array)
            {
                foreach (var item in array)
                {
                    Mask(item);
                }
            }

            return;
        }

        foreach (var (name, value) in obj.ToList())
        {
            if (value is JsonValue leaf && leaf.GetValueKind() == JsonValueKind.String && SecretName().IsMatch(name) && leaf.ToString() is { Length: > 0 })
            {
                obj[name] = AppConfigurationMask.Value;
            }
            else
            {
                Mask(value);
            }
        }
    }

    [GeneratedRegex("password|secret|apikey|api_key|token|connectionstring", RegexOptions.IgnoreCase)]
    private static partial Regex SecretName();
}

internal static class JsonFlattening
{
    public static Dictionary<string, string?> Flatten(string prefix, JsonElement element)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        void Walk(string key, JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in value.EnumerateObject())
                    {
                        Walk($"{key}:{property.Name}", property.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        Walk($"{key}:{index++}", item);
                    }

                    break;
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    values[key] = null;
                    break;
                case JsonValueKind.String:
                    values[key] = value.GetString();
                    break;
                default:
                    values[key] = value.GetRawText();
                    break;
            }
        }

        Walk(prefix, element);
        return values;
    }
}
