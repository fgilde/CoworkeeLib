using Coworkee.Application.Localization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

internal sealed class MailTemplateHandlers(
    CoworkeeDbContext db,
    ICurrentUser currentUser,
    IMailTemplateCatalog catalog,
    IMailTemplateRenderer renderer,
    IMailSender sender,
    IUserDirectory users,
    MailConnectionResolver resolver,
    IMailUnsubscribeLinks unsubscribeLinks,
    IEnumerable<IDefaultLanguage> defaultLanguages,
    IEnumerable<IMachineTranslator> translators)
    : IHandler<GetMailTemplates, Result<IReadOnlyList<MailTemplateSummaryDto>>>,
      IHandler<GetMailTemplate, Result<MailTemplateDto>>,
      IHandler<SaveMailTemplate, Result>,
      IHandler<AddMailTemplateLanguage, Result<MailTemplateLanguageAddedDto>>,
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
            var defaults = custom is null ? d.Defaults.Keys.ToList() : [];
            summaries.Add(new MailTemplateSummaryDto(
                d.Name,
                d.DisplayName,
                defaults.Concat(cultures).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList(),
                cultures,
                custom is not null,
                custom?.Description,
                defaults.Order(StringComparer.Ordinal).ToList()));
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
            : MailCultures.Candidates(query.Culture, await defaultLanguages.GetAsync(cancellationToken)).Select(c => definition.Defaults.GetValueOrDefault(c)).First(c => c is not null)!;
        return new MailTemplateDto(
            query.Name,
            query.Culture,
            existing?.Subject ?? fallback.Subject,
            existing?.Body ?? fallback.Body,
            fallback.Subject,
            fallback.Body,
            existing is not null,
            MailTemplateVariables.From(definition.SampleModel),
            catalog.IsCustom(definition));
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

    public async Task<Result<MailTemplateLanguageAddedDto>> HandleAsync(AddMailTemplateLanguage command, CancellationToken cancellationToken)
    {
        if (await catalog.FindAsync(command.Name, cancellationToken) is not { } definition)
        {
            return MailErrors.UnknownTemplate;
        }

        var (culture, from) = (command.Culture, command.Request.From);
        if ((MailErrors.CheckCulture(culture) ?? MailErrors.CheckCulture(from)) is { } error)
        {
            return error;
        }

        var owner = await OwnerAsync(definition, cancellationToken);
        if (await CheckWriteAsync(definition, owner, cancellationToken) is { } forbidden)
        {
            return forbidden;
        }

        if (definition.Defaults.Keys.Contains(culture, StringComparer.OrdinalIgnoreCase) || await FindOverrideAsync(command.Name, culture, owner, cancellationToken) is not null)
        {
            return Error.Validation("Culture", $"The template already has the language '{culture}'.");
        }

        var source = await renderer.ResolveAsync(command.Name, from, cancellationToken);
        var translated = command.Request.Translate && translators.FirstOrDefault() is { } translator
            ? await MailTemplateTranslation.TranslateAsync(source, from, culture, translator, cancellationToken)
            : null;
        var content = translated ?? source;
        db.Add(new MailTemplateOverride { Name = command.Name, Culture = culture, TenantId = owner, Subject = content.Subject, Body = content.Body });
        return new MailTemplateLanguageAddedDto(culture, from, translated is not null);
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

        if (await FindOverrideAsync(command.Name, command.Culture, owner, cancellationToken) is not { } existing)
        {
            return Result.Success();
        }

        if (catalog.IsCustom(definition) && await db.Set<MailTemplateOverride>().CountAsync(o => o.Name == command.Name && o.TenantId == owner, cancellationToken) == 1)
        {
            return Error.Validation("Culture", "A custom template keeps at least one language.");
        }

        db.Remove(existing);
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

        // a custom template is a newsletter: its footer links the real opt-out of the tester, not the sample address
        var model = catalog.IsCustom(definition) ? MailRecipientModel.SampleFor(unsubscribeLinks.Create(userId, currentUser.TenantId)) : definition.SampleModel;
        await sender.QueueAsync(email, command.Name, model, command.Culture, cancellationToken);
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
