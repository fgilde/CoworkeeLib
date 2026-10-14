using System.Text.RegularExpressions;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

[RequiresPermission(MailPermissions.Templates.Manage)]
public sealed record CreateMailTemplate(CreateMailTemplateRequest Request) : ICommand<Result>;

[RequiresPermission(MailPermissions.Templates.Manage)]
public sealed record DeleteMailTemplate(string Name) : ICommand<Result>;

internal static partial class CustomMailTemplates
{
    public static readonly Error SystemOnly = Error.Forbidden("mail.template_global_system_only", "Only the system organisation manages global mail templates.");

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    public static partial Regex NamePattern();

    public static async Task<CustomMailTemplate?> FindCustomAsync(this CoworkeeDbContext db, Guid? tenantId, string name, CancellationToken cancellationToken) =>
        (await db.Set<CustomMailTemplate>().Where(t => t.Name == name && (t.TenantId == null || t.TenantId == tenantId)).ToListAsync(cancellationToken))
            .OrderByDescending(t => t.TenantId != null).FirstOrDefault();
}

internal sealed class CustomMailTemplateHandlers(
    CoworkeeDbContext db,
    ICurrentUser currentUser,
    IMailTemplateCatalog catalog,
    IMailTemplateRenderer renderer,
    MailConnectionResolver resolver)
    : IHandler<CreateMailTemplate, Result>,
      IHandler<DeleteMailTemplate, Result>
{
    public async Task<Result> HandleAsync(CreateMailTemplate command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var name = request.Name?.Trim() ?? string.Empty;
        if (CheckInput(name, request.DisplayName) is { } invalid)
        {
            return invalid;
        }

        if (request.Global && !await resolver.IsSystemAsync(cancellationToken))
        {
            return CustomMailTemplates.SystemOnly;
        }

        if (await catalog.FindAsync(name, cancellationToken) is not null || (request.Global && await db.Set<CustomMailTemplate>().AnyAsync(t => t.Name == name, cancellationToken)))
        {
            return Error.Validation("Name", $"A mail template named '{name}' already exists.");
        }

        var tenantId = request.Global ? null : currentUser.TenantId;
        if (request.CopyFrom is { Length: > 0 } source && !await CopyAsync(source, name, tenantId, cancellationToken))
        {
            return MailErrors.UnknownTemplate;
        }

        db.Add(new CustomMailTemplate { Name = name, DisplayName = request.DisplayName.Trim(), Description = request.Description, TenantId = tenantId });
        return Result.Success();
    }

    public async Task<Result> HandleAsync(DeleteMailTemplate command, CancellationToken cancellationToken)
    {
        if (await catalog.FindAsync(command.Name, cancellationToken) is not { } definition)
        {
            return MailErrors.UnknownTemplate;
        }

        if (!catalog.IsCustom(definition))
        {
            return Error.Validation("Name", "Registered mail templates cannot be deleted.");
        }

        var template = await db.FindCustomAsync(currentUser.TenantId, command.Name, cancellationToken);
        if (template is null)
        {
            return MailErrors.UnknownTemplate;
        }

        if (template.TenantId is null && !await resolver.IsSystemAsync(cancellationToken))
        {
            return CustomMailTemplates.SystemOnly;
        }

        var tenantId = template.TenantId;
        db.RemoveRange(await db.Set<MailTemplateOverride>().Where(o => o.Name == command.Name && o.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Remove(template);
        return Result.Success();
    }

    private static Error? CheckInput(string name, string? displayName) =>
        name.Length is 0 or > 128 || !CustomMailTemplates.NamePattern().IsMatch(name)
            ? Error.Validation("Name", "Use 1-128 letters, digits, '.', '_' or '-'.")
            : string.IsNullOrWhiteSpace(displayName) || displayName.Length > 256
                ? Error.Validation("DisplayName", "A display name of up to 256 characters is required.")
                : null;

    private async Task<bool> CopyAsync(string source, string name, Guid? tenantId, CancellationToken cancellationToken)
    {
        if (await catalog.FindAsync(source, cancellationToken) is not { } definition)
        {
            return false;
        }

        var tenant = currentUser.TenantId;
        var stored = await db.Set<MailTemplateOverride>().Where(o => o.Name == source && (o.TenantId == null || o.TenantId == tenant))
            .Select(o => o.Culture).ToListAsync(cancellationToken);
        foreach (var culture in definition.Defaults.Keys.Concat(stored).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var content = await renderer.ResolveAsync(source, culture, cancellationToken);
            db.Add(new MailTemplateOverride { Name = name, Culture = culture, TenantId = tenantId, Subject = content.Subject, Body = content.Body });
        }

        return true;
    }
}
