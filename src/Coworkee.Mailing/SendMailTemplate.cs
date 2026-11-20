using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coworkee.Application.Authorization;
using Coworkee.Application.Events;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;

namespace Coworkee.Mailing;

/// <summary>Sends a template for real: one mail per recipient through the mail log; returns how many were queued.</summary>
[RequiresPermission(MailPermissions.Templates.Manage)]
[RequiresPermission(MailPermissions.Campaigns.Send)]
[AiTool(Exclude = true)]
[Description("Sends a mail template for real to users chosen by a filter of the Users set, added or left out by hand, with the given model values.")]
public sealed record SendMailTemplate(string Name, SendMailTemplateRequest Request) : ICommand<Result<int>>;

/// <summary>How many users a send for real reaches and the mail the first of them gets.</summary>
[RequiresPermission(MailPermissions.Templates.Manage)]
[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record PreviewMailTemplateSend(string Name, SendMailTemplateRequest Request) : IQuery<Result<MailTemplateSendPreviewDto>>;

internal sealed class SendMailTemplateValidator : AbstractValidator<SendMailTemplate>
{
    public SendMailTemplateValidator()
    {
        RuleFor(c => c.Request).NotNull();
        When(c => c.Request is not null, () =>
        {
            RuleFor(c => c.Request.Recipients).NotNull().SetValidator(new UserSelectionValidator());
            RuleFor(c => c.Request.Model).MaximumLength(MailTemplateSend.MaxModelLength);
        });
    }
}

internal sealed class PreviewMailTemplateSendValidator : AbstractValidator<PreviewMailTemplateSend>
{
    public PreviewMailTemplateSendValidator()
    {
        RuleFor(c => c.Request).NotNull();
        When(c => c.Request is not null, () =>
        {
            RuleFor(c => c.Request.Recipients).NotNull().SetValidator(new UserSelectionValidator());
            RuleFor(c => c.Request.Model).MaximumLength(MailTemplateSend.MaxModelLength);
        });
    }
}

internal sealed class MailTemplateSend(
    CoworkeeDbContext db, IMailTemplateCatalog catalog, IMailRecipientDirectory directory, IMailTemplateRenderer renderer, IMailUnsubscribeLinks links,
    ICurrentUser currentUser, TimeProvider clock)
    : IHandler<SendMailTemplate, Result<int>>,
      IHandler<PreviewMailTemplateSend, Result<MailTemplateSendPreviewDto>>
{
    /// <summary>More recipients go through a campaign (a job, page by page) instead of one request.</summary>
    public const int MaxRecipients = 1000;

    public const int MaxModelLength = 100_000;

    private static readonly JsonSerializerOptions Names = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly Dictionary<string, (MailTemplateContent Content, MailTemplateContent Layout)> _templates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The sample model with the names the template reads (<c>OldValue</c> is <c>old_value</c>), as JSON.</summary>
    public static string SampleJson(object sample) => JsonSerializer.Serialize(sample, Names);

    public async Task<Result<MailTemplateSendPreviewDto>> HandleAsync(PreviewMailTemplateSend query, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(query.Name, query.Request, cancellationToken);
        if (!prepared.IsSuccess)
        {
            return prepared.Error!;
        }

        var (model, source) = prepared.Value;
        var preview = await directory.PreviewAsync(source, 0, cancellationToken);
        if (!preview.IsSuccess)
        {
            return preview.Error!;
        }

        var first = await directory.PageAsync(source, null, 1, cancellationToken);
        if (!first.IsSuccess)
        {
            return first.Error!;
        }

        var mail = first.Value.Recipients is [var recipient, ..] ? await RenderAsync(query.Name, model, recipient, cancellationToken) : null;
        return new MailTemplateSendPreviewDto(preview.Value.Count, preview.Value.Excluded, mail is null ? null : new RenderedMailDto(mail.Subject, mail.HtmlBody));
    }

    public async Task<Result<int>> HandleAsync(SendMailTemplate command, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(command.Name, command.Request, cancellationToken);
        if (!prepared.IsSuccess)
        {
            return prepared.Error!;
        }

        var (model, source) = prepared.Value;
        var preview = await directory.PreviewAsync(source, 0, cancellationToken);
        if (!preview.IsSuccess)
        {
            return preview.Error!;
        }

        if (preview.Value.Count > MaxRecipients)
        {
            return Error.Validation("Recipients", $"A template goes to at most {MaxRecipients} users at once; send a campaign to more.");
        }

        // the unit of work saves the mails together: all or none
        var queued = 0;
        Guid? after = null;
        do
        {
            var page = await directory.PageAsync(source, after, 500, cancellationToken);
            if (!page.IsSuccess)
            {
                return page.Error!;
            }

            foreach (var recipient in page.Value.Recipients)
            {
                var rendered = await RenderAsync(command.Name, model, recipient, cancellationToken);
                db.Add(OutgoingMail.Queue(recipient.Email, rendered, command.Name, currentUser.TenantId, clock.GetUtcNow()));
                queued++;
            }

            after = page.Value.Next;
        }
        while (after is not null);

        return queued;
    }

    private async Task<Result<(JsonObject Model, RecipientSourceDto Source)>> PrepareAsync(string name, SendMailTemplateRequest request, CancellationToken cancellationToken)
    {
        if (name == CoreMailTemplates.Layout || await catalog.FindAsync(name, cancellationToken) is not { } definition)
        {
            return MailErrors.UnknownTemplate;
        }

        JsonObject? model;
        try
        {
            model = JsonNode.Parse(request.Model ?? SampleJson(definition.SampleModel)) as JsonObject;
        }
        catch (JsonException)
        {
            model = null;
        }

        return model is null
            ? Error.Validation(nameof(SendMailTemplateRequest.Model), "The model must be a JSON object.")
            : (model, new RecipientSourceDto(RecipientSourceKind.Selection) { Selection = request.Recipients });
    }

    // template and layout once per language, as campaigns do
    private async Task<RenderedMail> RenderAsync(string name, JsonObject model, MailRecipient recipient, CancellationToken cancellationToken)
    {
        var key = recipient.Culture ?? string.Empty;
        if (!_templates.TryGetValue(key, out var template))
        {
            template = _templates[key] = (await renderer.ResolveAsync(name, recipient.Culture, cancellationToken), await renderer.ResolveAsync(CoreMailTemplates.Layout, recipient.Culture, cancellationToken));
        }

        return await renderer.RenderContentAsync(template.Content, template.Layout, For(model, recipient), cancellationToken);
    }

    /// <summary>The model of one recipient: his values in <c>user</c>, his opt-out link in <c>unsubscribe_url</c> where the model has these.</summary>
    private JsonElement For(JsonObject model, MailRecipient recipient)
    {
        var mine = (JsonObject)model.DeepClone();
        if (mine["user"] is JsonObject user)
        {
            var displayName = $"{recipient.FirstName} {recipient.LastName}".Trim();
            user["first_name"] = recipient.FirstName;
            user["last_name"] = recipient.LastName;
            user["display_name"] = displayName.Length > 0 ? displayName : recipient.Email;
            user["email"] = recipient.Email;
            user["culture"] = recipient.Culture;
        }

        if (mine.ContainsKey("unsubscribe_url"))
        {
            mine["unsubscribe_url"] = UnsubscribeUrl(recipient.Id);
        }

        return JsonSerializer.SerializeToElement(mine);
    }

    private string? UnsubscribeUrl(Guid userId)
    {
        try
        {
            return links.Create(userId, currentUser.TenantId);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
