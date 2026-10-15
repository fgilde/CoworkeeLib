using System.Text.Json;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Coworkee.Auditing.History;

/// <summary>What a person reads for a recorded value: enum names, the name of a referenced record the user may see, plain text.</summary>
internal sealed class EntityHistoryTexts(CoworkeeDbContext db, IServiceProvider services, IEntityType type)
{
    private static readonly string[] NameProperties = ["Name", "Title", "DisplayName", "Subject", "UserName", "Email", "Code", "Text"];
    private readonly Dictionary<(Type, Guid), string> _names = [];

    public async Task LoadAsync(IEnumerable<(string Property, string? Json)> values, CancellationToken cancellationToken)
    {
        var references = values
            .Select(v => (Principal: Principal(v.Property), Id: Id(v.Json)))
            .Where(r => r.Principal is not null && r.Id is not null)
            .GroupBy(r => r.Principal!, r => r.Id!.Value);
        foreach (var group in references)
        {
            if (!await ODataEntityAccess.IsGrantedAsync(services, group.Key, cancellationToken))
            {
                continue;
            }

            var load = typeof(EntityHistoryTexts).GetMethod(nameof(NamesAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(group.Key);
            foreach (var (id, name) in await (Task<Dictionary<Guid, string>>)load.Invoke(this, [group.Distinct().ToList(), cancellationToken])!)
            {
                _names[(group.Key, id)] = name;
            }
        }
    }

    public string? Text(string property, string? json)
    {
        if (json is null)
        {
            return null;
        }

        if (json == EntityHistoryFields.Masked)
        {
            return "***";
        }

        if (Principal(property) is { } principal && Id(json) is { } id && _names.TryGetValue((principal, id), out var name))
        {
            return name;
        }

        var clr = type.FindProperty(property)?.ClrType is { } t ? Nullable.GetUnderlyingType(t) ?? t : null;
        try
        {
            if (clr is { IsEnum: true })
            {
                return Contracts.Data.EnumLabels.Of((Enum)JsonSerializer.Deserialize(json, clr)!);
            }

            var element = JsonSerializer.Deserialize<JsonElement>(json);
            return element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>The name a record goes by, from the first of the usual name properties it has.</summary>
    public static string? Describe(object entity) =>
        NameProperties.Select(n => entity.GetType().GetProperty(n)?.GetValue(entity) as string).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private Type? Principal(string property) =>
        type.FindProperty(property)?.GetContainingForeignKeys().FirstOrDefault(k => k.Properties.Count == 1)?.PrincipalEntityType is { } principal
        && !principal.IsOwned() && principal.FindPrimaryKey()?.Properties is [{ Name: "Id" } key] && key.ClrType == typeof(Guid)
            ? principal.ClrType
            : null;

    private static Guid? Id(string? json)
    {
        try
        {
            return json is null ? null : JsonSerializer.Deserialize<Guid?>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<Dictionary<Guid, string>> NamesAsync<T>(List<Guid> ids, CancellationToken cancellationToken)
        where T : class
    {
        var rows = await db.Set<T>().AsNoTracking().IgnoreQueryFilters([CoworkeeDbContext.SoftDeleteFilter])
            .Where(e => ids.Contains(EF.Property<Guid>(e, "Id")))
            .ToListAsync(cancellationToken);
        return rows
            .Select(r => (Id: typeof(T).GetProperty("Id")?.GetValue(r) as Guid?, Name: Describe(r)))
            .Where(r => r.Id is not null && r.Name is not null)
            .ToDictionary(r => r.Id!.Value, r => r.Name!);
    }
}
