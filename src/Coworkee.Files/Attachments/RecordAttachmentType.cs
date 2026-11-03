using Coworkee.Infrastructure.Persistence;

namespace Coworkee.Files.Attachments;

/// <summary>The slots of one record type and who may change them.</summary>
public abstract class RecordAttachmentType(string entityType)
{
    public const long DefaultMaxBytes = 10 * 1024 * 1024;

    public string EntityType { get; } = entityType;

    internal Dictionary<string, AttachmentSlot> Slots { get; } = new(StringComparer.Ordinal);

    internal string? WritePermission { get; private set; }

    internal List<Func<IServiceProvider, AttachmentAccess, CancellationToken, Task<bool>>> Checks { get; } = [];

    /// <summary>A slot of one file; an upload replaces it.</summary>
    public RecordAttachmentType Slot(string name, long maxBytes = DefaultMaxBytes, params string[] contentTypes) => Add(new(name, maxBytes, contentTypes, false));

    /// <summary>A slot of any number of files.</summary>
    public RecordAttachmentType MultiSlot(string name, long maxBytes = DefaultMaxBytes, params string[] contentTypes) => Add(new(name, maxBytes, contentTypes, true));

    /// <summary>Uploads and deletes need this permission on top of seeing the record.</summary>
    public RecordAttachmentType WriteRequires(string permission)
    {
        WritePermission = permission;
        return this;
    }

    /// <summary>An extra check on reads (<c>Write == false</c>) and writes, on top of the record's visibility; every check must pass.</summary>
    public RecordAttachmentType Authorize(Func<IServiceProvider, AttachmentAccess, CancellationToken, Task<bool>> check)
    {
        Checks.Add(check);
        return this;
    }

    /// <summary>The ids the current user sees: through the OData set of the type, else every row of the own organisation; null when he may not see the type.</summary>
    internal abstract Task<IQueryable<Guid>?> VisibleIdsAsync(IServiceProvider services, CancellationToken cancellationToken);

    /// <summary>The ids of every row, deleted or not, of all organisations: the records that still have a claim on their files.</summary>
    /// <summary>The ids of the records in the app's database, null for records of an external database.</summary>
    internal abstract IQueryable<Guid>? ExistingIds(CoworkeeDbContext db);

    private RecordAttachmentType Add(AttachmentSlot slot)
    {
        if (slot.Name.Length is 0 or > 50 || !slot.Name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new ArgumentException($"'{slot.Name}' is not a valid slot name: letters, digits, '-' and '_', at most 50.", nameof(slot));
        }

        Slots[slot.Name] = slot;
        return this;
    }
}
