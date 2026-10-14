using System.Globalization;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

[RequiresPermission(MailPermissions.Templates.Manage)]
public sealed record GetMailTemplates : IQuery<Result<IReadOnlyList<MailTemplateSummaryDto>>>;

[RequiresPermission(MailPermissions.Templates.Manage)]
public sealed record GetMailTemplate(string Name, string Culture) : IQuery<Result<MailTemplateDto>>;

[RequiresPermission(MailPermissions.Templates.Manage)]
public sealed record SaveMailTemplate(string Name, string Culture, SaveMailTemplateRequest Content) : ICommand<Result>;

[RequiresPermission(MailPermissions.Templates.Manage)]
public sealed record ResetMailTemplate(string Name, string Culture) : ICommand<Result>;

[RequiresPermission(MailPermissions.Templates.Manage)]
public sealed record PreviewMailTemplate(string Name, string Culture, SaveMailTemplateRequest Content) : IQuery<Result<RenderedMailDto>>;

[RequiresPermission(MailPermissions.Templates.Manage)]
public sealed record SendTestMail(string Name, string Culture) : ICommand<Result>;

[RequiresPermission(MailPermissions.Log.View)]
public sealed record GetOutgoingMails(PageRequest Page, OutgoingMailStatus? Status) : IQuery<Result<PagedResult<OutgoingMailDto>>>;

internal static class MailErrors
{
    public static readonly Error UnknownTemplate = Error.NotFound("mail.template_not_found", "The mail template does not exist.");

    public static Error? CheckCulture(string culture)
    {
        try
        {
            _ = CultureInfo.GetCultureInfo(culture, predefinedOnly: true);
            return null;
        }
        catch (CultureNotFoundException)
        {
            return Error.Validation("Culture", $"Unknown culture '{culture}'.");
        }
    }

    public static Error? CheckSyntax(SaveMailTemplateRequest content) =>
        MailTemplateValidation.Validate(content.Subject, content.Body) is { Count: > 0 } errors
            ? Error.Validation("Body", string.Join(Environment.NewLine, errors))
            : null;
}

internal sealed class MailTemplateHandlers(
    CoworkeeDbContext db,
    ICurrentUser currentUser,
    IMailTemplateCatalog catalog,
    IMailTemplateRenderer renderer,
    IMailSender sender,
    IUserDirectory users,
    MailConnectionResolver resolver)
    : IHandler<GetMailTemplates, Result<IReadOnlyList<MailTemplateSummaryDto>>>,
      IHandler<GetMailTemplate, Result<MailTemplateDto>>,
      IHandler<SaveMailTemplate, Result>,
      IHandler<ResetMailTemplate, Result>,
      IHandler<PreviewMailTemplate, Result<RenderedMailDto>>,
      IHandler<SendTestMail, Result>
{
    public async Task<Result<IReadOnlyList<MailTemplateSummaryDto>>> HandleAsync(GetMailTemplates query, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var overridden = await db.Set<MailTemplateOverride>().AsNoTracking().Where(o => o.TenantId == tenantId || o.TenantId == null)
            .Select(o => new { o.Name, o.Culture, o.TenantId }).ToListAsync(cancellationToken);
        var customs = await db.Set<CustomMailTemplate>().AsNoTracking().Where(t => t.TenantId == tenantId || t.TenantId == null).ToListAsync(cancellationToken);
        var summaries = new List<MailTemplateSummaryDto>();
        foreach (var d in (await catalog.AllAsync(cancellationToken)).OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            var custom = catalog.IsCustom(d) ? customs.OrderByDescending(t => t.TenantId != null).FirstOrDefault(t => t.Name == d.Name) : null;
            var owner = custom is null ? tenantId : custom.TenantId;
            var cultures = overridden.Where(o => o.Name == d.Name && o.TenantId == owner).Select(o => o.Culture).ToList();
            summaries.Add(new MailTemplateSummaryDto(
                d.Name,
                d.DisplayName,
                custom is null ? d.Defaults.Keys.Order(StringComparer.Ordinal).ToList() : cultures.Order(StringComparer.Ordinal).ToList(),
                cultures,
                custom is not null,
                custom?.Description));
        }

        return Result<IReadOnlyList<MailTemplateSummaryDto>>.Success(summaries);
    }

    public async Task<Result<MailTemplateDto>> HandleAsync(GetMailTemplate query, CancellationToken cancellationToken)
    {
        if (await catalog.FindAsync(query.Name, cancellationToken) is not { } definition)
        {
            return MailErrors.UnknownTemplate;
        }

        var owner = await OwnerAsync(definition, cancellationToken);
        var existing = await FindOverrideAsync(query.Name, query.Culture, owner, cancellationToken);
        var fallback = catalog.IsCustom(definition)
            ? await OtherCultureAsync(query.Name, query.Culture, owner, cancellationToken)
            : MailCultures.Candidates(query.Culture).Select(c => definition.Defaults.GetValueOrDefault(c)).First(c => c is not null)!;
        return new MailTemplateDto(
            query.Name,
            query.Culture,
            existing?.Subject ?? fallback.Subject,
            existing?.Body ?? fallback.Body,
            fallback.Subject,
            fallback.Body,
            existing is not null,
            MailTemplateVariables.From(definition.SampleModel));
    }

    public async Task<Result> HandleAsync(SaveMailTemplate command, CancellationToken cancellationToken)
    {
        if (await catalog.FindAsync(command.Name, cancellationToken) is not { } definition)
        {
            return MailErrors.UnknownTemplate;
        }

        if ((MailErrors.CheckCulture(command.Culture) ?? MailErrors.CheckSyntax(command.Content)) is { } error)
        {
            return error;
        }

        var owner = await OwnerAsync(definition, cancellationToken);
        if (await CheckWriteAsync(definition, owner, cancellationToken) is { } forbidden)
        {
            return forbidden;
        }

        if (await FindOverrideAsync(command.Name, command.Culture, owner, cancellationToken) is { } existing)
        {
            existing.Subject = command.Content.Subject;
            existing.Body = command.Content.Body;
        }
        else
        {
            db.Add(new MailTemplateOverride
            {
                Name = command.Name,
                Culture = command.Culture,
                TenantId = owner,
                Subject = command.Content.Subject,
                Body = command.Content.Body,
            });
        }

        return Result.Success();
    }

    public async Task<Result> HandleAsync(ResetMailTemplate command, CancellationToken cancellationToken)
    {
        if (await catalog.FindAsync(command.Name, cancellationToken) is not { } definition)
        {
            return Result.Success();
        }

        var owner = await OwnerAsync(definition, cancellationToken);
        if (await CheckWriteAsync(definition, owner, cancellationToken) is { } forbidden)
        {
            return forbidden;
        }

        if (await FindOverrideAsync(command.Name, command.Culture, owner, cancellationToken) is { } existing)
        {
            db.Remove(existing);
        }

        return Result.Success();
    }

    public async Task<Result<RenderedMailDto>> HandleAsync(PreviewMailTemplate query, CancellationToken cancellationToken)
    {
        if (await catalog.FindAsync(query.Name, cancellationToken) is not { } definition)
        {
            return MailErrors.UnknownTemplate;
        }

        if (MailErrors.CheckSyntax(query.Content) is { } error)
        {
            return error;
        }

        var rendered = await renderer.RenderContentAsync(new MailTemplateContent(query.Content.Subject, query.Content.Body), definition.SampleModel, query.Culture, cancellationToken);
        return new RenderedMailDto(rendered.Subject, rendered.HtmlBody);
    }

    public async Task<Result> HandleAsync(SendTestMail command, CancellationToken cancellationToken)
    {
        if (await catalog.FindAsync(command.Name, cancellationToken) is not { } definition)
        {
            return MailErrors.UnknownTemplate;
        }

        if (currentUser.UserId is not { } userId || await users.GetEmailAsync(userId, cancellationToken) is not { Length: > 0 } email)
        {
            return Error.Validation("User", "The current user has no email address.");
        }

        await sender.QueueAsync(email, command.Name, definition.SampleModel, command.Culture, cancellationToken);
        return Result.Success();
    }

    private async Task<Guid?> OwnerAsync(MailTemplateDefinition definition, CancellationToken cancellationToken) =>
        catalog.IsCustom(definition) && await db.FindCustomAsync(currentUser.TenantId, definition.Name, cancellationToken) is { } custom
            ? custom.TenantId
            : currentUser.TenantId;

    private async Task<Error?> CheckWriteAsync(MailTemplateDefinition definition, Guid? owner, CancellationToken cancellationToken) =>
        catalog.IsCustom(definition) && owner is null && !await resolver.IsSystemAsync(cancellationToken) ? CustomMailTemplates.SystemOnly : null;

    private async Task<MailTemplateContent> OtherCultureAsync(string name, string culture, Guid? owner, CancellationToken cancellationToken)
    {
        var rows = await db.Set<MailTemplateOverride>().AsNoTracking().Where(o => o.Name == name && o.TenantId == owner && o.Culture != culture).ToListAsync(cancellationToken);
        return rows.OrderBy(o => o.CreatedAt).FirstOrDefault() is { } first ? new MailTemplateContent(first.Subject, first.Body) : new MailTemplateContent(string.Empty, string.Empty);
    }

    private Task<MailTemplateOverride?> FindOverrideAsync(string name, string culture, Guid? tenantId, CancellationToken cancellationToken) =>
        db.Set<MailTemplateOverride>().SingleOrDefaultAsync(o => o.TenantId == tenantId && o.Name == name && o.Culture == culture, cancellationToken);
}
internal sealed class OutgoingMailHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<GetOutgoingMails, Result<PagedResult<OutgoingMailDto>>>
{
    public async Task<Result<PagedResult<OutgoingMailDto>>> HandleAsync(GetOutgoingMails query, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var mails = db.Set<OutgoingMail>().AsNoTracking().Where(m => m.TenantId == tenantId);
        if (query.Status is { } status)
        {
            mails = mails.Where(m => m.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Page.Search))
        {
            var search = query.Page.Search.Trim();
            mails = mails.Where(m => m.To.Contains(search) || m.Subject.Contains(search));
        }

        var total = await mails.CountAsync(cancellationToken);
        var items = await mails.OrderByDescending(m => m.QueuedAt)
            .Skip((query.Page.Page - 1) * query.Page.PageSize)
            .Take(query.Page.PageSize)
            .Select(m => new OutgoingMailDto(m.Id, m.To, m.Subject, m.TemplateName, m.Status, m.Attempts, m.LastError, m.QueuedAt, m.SentAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<OutgoingMailDto>(items, total, query.Page.Page, query.Page.PageSize);
    }
}
