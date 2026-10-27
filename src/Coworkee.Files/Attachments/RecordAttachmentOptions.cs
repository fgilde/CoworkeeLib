using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Files.Attachments;

/// <summary>What is about to happen on the attachments of one record: read (list, download) or write (upload, delete).</summary>
public sealed record AttachmentAccess(string EntityType, Guid EntityId, string Slot, bool Write);

/// <param name="ContentTypes">Accepted types such as <c>application/pdf</c> or <c>image/*</c>; empty takes any.</param>
/// <param name="Multiple">False: an upload replaces the file of the slot.</param>
public sealed record AttachmentSlot(string Name, long MaxBytes, IReadOnlyList<string> ContentTypes, bool Multiple)
{
    public bool Accepts(string contentType) =>
        ContentTypes.Count == 0 || ContentTypes.Any(t => t.EndsWith("/*", StringComparison.Ordinal)
            ? contentType.StartsWith(t[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(t, contentType, StringComparison.OrdinalIgnoreCase));
}

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

internal sealed class RecordAttachmentType<TEntity>(string entityType) : RecordAttachmentType(entityType)
    where TEntity : class
{
    internal override Task<IQueryable<Guid>?> VisibleIdsAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        ODataEntityAccess.VisibleIdsAsync<TEntity>(services, cancellationToken);

    internal override IQueryable<Guid>? ExistingIds(CoworkeeDbContext db) => db.Model.FindEntityType(typeof(TEntity)) is null ? null : db.Set<TEntity>().IgnoreQueryFilters().Select(e => EF.Property<Guid>(e, "Id"));
}

/// <summary>The record types that take attachments, by the name used in the address (usually the OData set name).</summary>
public sealed class RecordAttachmentOptions
{
    internal Dictionary<string, RecordAttachmentType> Types { get; } = new(StringComparer.Ordinal);

    public RecordAttachmentType? Find(string entityType) => Types.GetValueOrDefault(entityType);
}

public static class RecordAttachmentServiceCollectionExtensions
{
    /// <summary>
    /// Attachments on <typeparamref name="TEntity"/>, e.g. <c>services.AddRecordAttachments&lt;LeaveRequest&gt;("LeaveRequests", a => a.Slot("certificate", 5_000_000, "application/pdf", "image/*"))</c>.
    /// Reading follows the visibility of the type's OData set; writing also needs <see cref="RecordAttachmentType.WriteRequires"/> and <see cref="RecordAttachmentType.Authorize"/> when set.
    /// </summary>
    public static IServiceCollection AddRecordAttachments<TEntity>(this IServiceCollection services, string entityType, Action<RecordAttachmentType> configure)
        where TEntity : class
    {
        Coworkee.OData.ODataEntityAccess.RequireGuidId<TEntity>("Record attachments");
        if (services.FirstOrDefault(d => d.ServiceType == typeof(RecordAttachmentOptions))?.ImplementationInstance is not RecordAttachmentOptions options)
        {
            options = new RecordAttachmentOptions();
            services.AddSingleton(options);
        }

        services.AddRetentionTarget<OrphanedAttachmentRetention>();

        var type = new RecordAttachmentType<TEntity>(entityType);
        configure(type);
        options.Types[entityType] = type;
        return services;
    }
}
