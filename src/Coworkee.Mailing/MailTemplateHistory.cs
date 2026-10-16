using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

/// <summary>The saved states of a template language, read from the audit trail of its overrides (also those a reset removed), newest first.</summary>
[RequiresPermission(MailPermissions.Templates.Manage)]
public sealed record GetMailTemplateHistory(string Name, string Culture) : IQuery<Result<IReadOnlyList<MailTemplateVersionDto>>>;

internal sealed class MailTemplateHistoryHandler(CoworkeeDbContext db, IMailTemplateCatalog catalog, ICurrentUser currentUser, IUserDirectory users)
    : IHandler<GetMailTemplateHistory, Result<IReadOnlyList<MailTemplateVersionDto>>>
{
    public async Task<Result<IReadOnlyList<MailTemplateVersionDto>>> HandleAsync(GetMailTemplateHistory query, CancellationToken cancellationToken)
    {
        if (await catalog.FindAsync(query.Name, cancellationToken) is not { } definition)
        {
            return MailErrors.UnknownTemplate;
        }

        var owner = catalog.IsCustom(definition) && await db.FindCustomAsync(currentUser.TenantId, definition.Name, cancellationToken) is { } custom
            ? custom.TenantId
            : currentUser.TenantId;

        // ponytail: reads every override entry and matches name, culture and owner in memory (they sit in the JSON changes); filter in SQL if templates get thousands of saves
        var entries = await db.Set<AuditEntry>().AsNoTracking()
            .Where(e => e.EntityType == nameof(MailTemplateOverride))
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
        var created = entries
            .Where(e => e.Action == AuditAction.Created && Value(e, nameof(MailTemplateOverride.Name)) == Json(query.Name)
                && Value(e, nameof(MailTemplateOverride.Culture)) == Json(query.Culture) && Value(e, nameof(MailTemplateOverride.TenantId)) == Json(owner))
            .Select(e => e.EntityId)
            .ToHashSet(StringComparer.Ordinal);

        var versions = new List<(AuditEntry Entry, string Subject, string Body)>();
        foreach (var rows in entries.Where(e => created.Contains(e.EntityId)).GroupBy(e => e.EntityId))
        {
            string subject = string.Empty, body = string.Empty;
            foreach (var entry in rows.Where(e => e.Action is AuditAction.Created or AuditAction.Updated))
            {
                subject = Text(entry, nameof(MailTemplateOverride.Subject)) ?? subject;
                body = Text(entry, nameof(MailTemplateOverride.Body)) ?? body;
                versions.Add((entry, subject, body));
            }
        }

        var names = await users.GetDisplayNamesAsync([.. versions.Select(v => v.Entry.ActorId).OfType<Guid>().Distinct()], cancellationToken);
        IReadOnlyList<MailTemplateVersionDto> result = versions
            .OrderByDescending(v => v.Entry.OccurredAt).ThenByDescending(v => v.Entry.Id)
            .Select(v => new MailTemplateVersionDto(v.Entry.OccurredAt, v.Entry.ActorId is { } actor ? names.GetValueOrDefault(actor) : null, v.Subject, v.Body))
            .ToList();
        return Result<IReadOnlyList<MailTemplateVersionDto>>.Success(result);
    }

    private static string? Json<T>(T? value) => value is null ? null : JsonSerializer.Serialize(value);

    private static string? Value(AuditEntry entry, string property) => entry.Changes.FirstOrDefault(c => c.Property == property)?.NewValue;

    private static string? Text(AuditEntry entry, string property) => Value(entry, property) is { } json ? JsonSerializer.Deserialize<string>(json) : null;
}
