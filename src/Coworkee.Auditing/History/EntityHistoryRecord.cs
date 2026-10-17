using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coworkee.Contracts.Auditing;
using Coworkee.Core.Results;
using Coworkee.Domain;
using Coworkee.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Coworkee.Auditing.History;

/// <summary>A record with its audit entries, oldest first, and the values it had at any of them.</summary>
internal sealed class EntityHistoryRecord(
    EntityHistoryRegistration registration, object entity, EntityHistoryFields fields, EntityHistoryTexts texts, IReadOnlyList<AuditEntry> entries)
{
    public EntityHistoryRegistration Registration => registration;

    public object Entity => entity;

    public EntityHistoryFields Fields => fields;

    public EntityHistoryTexts Texts => texts;

    public IReadOnlyList<AuditEntry> Entries => entries;

    public bool IsDeleted => entity is ISoftDelete { IsDeleted: true };

    public IReadOnlyDictionary<string, string?> Current { get; } = fields.Read(entity);

    /// <summary>Changes with any value, the deleted state or a new entry; a restore compares it with the one of its preview.</summary>
    public string Version =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Current, IsDeleted, Last = entries.LastOrDefault()?.Id }))))[..32];

    /// <summary>
    /// The values the request asks for: after or before the entry later changes are rolled back from the current values;
    /// an undo sets only the entry's fields (or the one property) back.
    /// </summary>
    public (Dictionary<string, string?>? Values, Error? Error) Target(EntityHistoryRestoreRequest request)
    {
        var index = IndexOf(request.EntryId);
        if (index < 0)
        {
            return (null, HistoryErrors.NotFound);
        }

        var entry = entries[index];
        var undo = request.Property is not null || request.Mode == EntityHistoryRestoreMode.Undo;
        if (entry.Action == AuditAction.Created && request.Mode != EntityHistoryRestoreMode.After)
        {
            return (null, HistoryErrors.NotRestorable);
        }

        var target = new Dictionary<string, string?>(Current, StringComparer.Ordinal);
        if (request.Property is { } name)
        {
            if (entry.Changes.FirstOrDefault(c => c.Property == name) is not { } change
                || fields.Restorable(name) is not { } property
                || !fields.CanTake(property, change.OldValue))
            {
                return (null, HistoryErrors.NotRestorable);
            }

            target[property.Name] = change.OldValue;
            return (target, null);
        }

        var rolledBack = undo ? [entry] : entries.Skip(request.Mode == EntityHistoryRestoreMode.Before ? index : index + 1);
        foreach (var change in rolledBack.Reverse().SelectMany(e => e.Changes))
        {
            if (fields.Restorable(change.Property) is { } property && fields.CanTake(property, change.OldValue))
            {
                target[property.Name] = change.OldValue;
            }
        }

        return (target, null);
    }

    /// <summary>The properties an undo of the request would take from a later entry; empty for the other modes.</summary>
    public IReadOnlySet<string> ChangedLater(EntityHistoryRestoreRequest request) =>
        request.Property is null && request.Mode != EntityHistoryRestoreMode.Undo
            ? new HashSet<string>()
            : entries.Skip(IndexOf(request.EntryId) + 1).SelectMany(e => e.Changes).Select(c => c.Property).ToHashSet(StringComparer.Ordinal);

    private int IndexOf(Guid entryId) => entries.ToList().FindIndex(e => e.Id == entryId);

    public IEnumerable<IProperty> Differences(IReadOnlyDictionary<string, string?> target) =>
        fields.Properties.Where(p => !fields.Same(p, Current[p.Name], target[p.Name]));
}
