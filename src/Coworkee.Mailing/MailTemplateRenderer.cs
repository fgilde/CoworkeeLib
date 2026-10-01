using System.Collections;
using System.Net;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Scriban;
using Scriban.Runtime;

namespace Coworkee.Mailing;

public sealed record RenderedMail(string Subject, string HtmlBody);

public interface IMailTemplateRenderer
{
    Task<RenderedMail> RenderAsync(string name, object model, string? culture, CancellationToken cancellationToken);

    Task<RenderedMail> RenderContentAsync(MailTemplateContent content, object model, string? culture, CancellationToken cancellationToken);

    Task<MailTemplateContent> ResolveAsync(string name, string? culture, CancellationToken cancellationToken);
}

public static class MailTemplateValidation
{
    public static IReadOnlyList<string> Validate(string subject, string body) =>
        Errors("subject", subject).Concat(Errors("body", body)).ToList();

    private static IEnumerable<string> Errors(string part, string text)
    {
        var template = Template.Parse(text);
        return template.HasErrors ? template.Messages.Select(m => $"{part}: {m}") : [];
    }
}

internal sealed class MailTemplateRenderer(CoworkeeDbContext db, ICurrentUser currentUser, IMailTemplateDefinitionManager definitions) : IMailTemplateRenderer
{
    public async Task<RenderedMail> RenderAsync(string name, object model, string? culture, CancellationToken cancellationToken) =>
        await RenderContentAsync(await ResolveAsync(name, culture, cancellationToken), model, culture, cancellationToken);

    public async Task<RenderedMail> RenderContentAsync(MailTemplateContent content, object model, string? culture, CancellationToken cancellationToken)
    {
        var subject = (await RenderAsync(content.Subject, ToScript(model, escape: false))).Trim();
        var body = await RenderAsync(content.Body, ToScript(model, escape: true));
        var layout = await ResolveAsync(CoreMailTemplates.Layout, culture, cancellationToken);
        var layoutModel = new ScriptObject { ["subject"] = WebUtility.HtmlEncode(subject), ["content"] = body };
        return new RenderedMail(subject, await RenderAsync(layout.Body, layoutModel));
    }

    public async Task<MailTemplateContent> ResolveAsync(string name, string? culture, CancellationToken cancellationToken)
    {
        var definition = definitions.Find(name) ?? throw new ArgumentException($"Mail template '{name}' is not defined.", nameof(name));
        var candidates = MailCultures.Candidates(culture);
        var tenantId = currentUser.TenantId;
        var overrides = await db.Set<MailTemplateOverride>().AsNoTracking()
            .Where(o => o.Name == name && candidates.Contains(o.Culture) && (o.TenantId == null || o.TenantId == tenantId))
            .ToListAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            var match = overrides.FirstOrDefault(o => o.TenantId != null && Same(o.Culture, candidate))
                ?? overrides.FirstOrDefault(o => o.TenantId == null && Same(o.Culture, candidate));
            if (match is not null)
            {
                return new MailTemplateContent(match.Subject, match.Body);
            }

            if (definition.Defaults.FirstOrDefault(d => Same(d.Key, candidate)).Value is { } content)
            {
                return content;
            }
        }

        return definition.Defaults[MailCultures.Fallback];
    }

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> RenderAsync(string text, ScriptObject model)
    {
        var template = Template.Parse(text);
        if (template.HasErrors)
        {
            throw new InvalidOperationException("Mail template is invalid: " + string.Join("; ", template.Messages));
        }

        var context = new TemplateContext { MemberRenamer = StandardMemberRenamer.Default };
        context.PushGlobal(model);
        return await template.RenderAsync(context);
    }

    private static ScriptObject ToScript(object model, bool escape)
    {
        if (model is ScriptObject script)
        {
            return script;
        }

        var result = new ScriptObject();
        foreach (var property in model.GetType().GetProperties())
        {
            result[StandardMemberRenamer.Default(property)] = Convert(property.GetValue(model), escape);
        }

        return result;
    }

    private static object? Convert(object? value, bool escape) => value switch
    {
        null => null,
        string text => escape ? WebUtility.HtmlEncode(text) : text,
        Uri uri => escape ? WebUtility.HtmlEncode(uri.ToString()) : uri.ToString(),
        _ when value.GetType().IsPrimitive || value is decimal or DateTime or DateTimeOffset or Guid or Enum => value,
        IEnumerable items => new ScriptArray(items.Cast<object?>().Select(item => Convert(item, escape))),
        _ => ToScript(value, escape),
    };
}
