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

    /// <summary>The values after the entry: later changes are rolled back from the current values; with a property only that change is undone.</summary>
    public (Dictionary<string, string?>? Values, Error? Error) Target(EntityHistoryRestoreRequest request)
    {
        var index = entries.ToList().FindIndex(e => e.Id == request.EntryId);
        if (index < 0)
        {
            return (null, HistoryErrors.NotFound);
        }

        var target = new Dictionary<string, string?>(Current, StringComparer.Ordinal);
        if (request.Property is { } name)
        {
            if (entries[index].Action == AuditAction.Created
                || entries[index].Changes.FirstOrDefault(c => c.Property == name) is not { } change
                || fields.Restorable(name) is not { } property
                || !fields.CanTake(property, change.OldValue))
            {
                return (null, HistoryErrors.NotRestorable);
            }

            target[property.Name] = change.OldValue;
            return (target, null);
        }

        foreach (var change in entries.Skip(index + 1).Reverse().SelectMany(e => e.Changes))
        {
            if (fields.Restorable(change.Property) is { } property && fields.CanTake(property, change.OldValue))
            {
                target[property.Name] = change.OldValue;
            }
        }

        return (target, null);
    }

    public IEnumerable<IProperty> Differences(IReadOnlyDictionary<string, string?> target) =>
        fields.Properties.Where(p => !fields.Same(p, Current[p.Name], target[p.Name]));
}
