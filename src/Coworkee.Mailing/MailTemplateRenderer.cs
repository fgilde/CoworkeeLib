using System.Collections;
using System.Net;
using System.Text.Json;
using Coworkee.Application.Localization;
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

    /// <summary>Renders with an already resolved layout, for senders that render many mails of one template.</summary>
    Task<RenderedMail> RenderContentAsync(MailTemplateContent content, MailTemplateContent layout, object model, CancellationToken cancellationToken);

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

internal sealed class MailTemplateRenderer(CoworkeeDbContext db, ICurrentUser currentUser, IMailTemplateCatalog catalog, IEnumerable<IDefaultLanguage> defaultLanguages) : IMailTemplateRenderer
{
    public async Task<RenderedMail> RenderAsync(string name, object model, string? culture, CancellationToken cancellationToken) =>
        await RenderContentAsync(await ResolveAsync(name, culture, cancellationToken), model, culture, cancellationToken);

    public async Task<RenderedMail> RenderContentAsync(MailTemplateContent content, object model, string? culture, CancellationToken cancellationToken) =>
        await RenderContentAsync(content, await ResolveAsync(CoreMailTemplates.Layout, culture, cancellationToken), model, cancellationToken);

    public async Task<RenderedMail> RenderContentAsync(MailTemplateContent content, MailTemplateContent layout, object model, CancellationToken cancellationToken)
    {
        var plain = ToScript(model, escape: false);
        var subject = (await RenderAsync(content.Subject, plain)).Trim();
        var body = await RenderAsync(content.Body, ToScript(model, escape: true));
        var layoutModel = new ScriptObject { ["subject"] = WebUtility.HtmlEncode(subject), ["content"] = body };
        if (plain.TryGetValue("unsubscribe_url", out var unsubscribe) && unsubscribe is string { Length: > 0 } link)
        {
            layoutModel["unsubscribe_url"] = WebUtility.HtmlEncode(link);
        }

        return new RenderedMail(subject, await RenderAsync(layout.Body, layoutModel));
    }

    public async Task<MailTemplateContent> ResolveAsync(string name, string? culture, CancellationToken cancellationToken)
    {
        var definition = await catalog.FindAsync(name, cancellationToken) ?? throw new ArgumentException($"Mail template '{name}' is not defined.", nameof(name));
        var custom = catalog.IsCustom(definition);
        var candidates = MailCultures.Candidates(culture, await defaultLanguages.GetAsync(cancellationToken));
        var tenantId = currentUser.TenantId;
        var stored = db.Set<MailTemplateOverride>().AsNoTracking().Where(o => o.Name == name && (o.TenantId == null || o.TenantId == tenantId));
        if (!custom)
        {
            stored = stored.Where(o => candidates.Contains(o.Culture));
        }

        var overrides = (await stored.ToListAsync(cancellationToken)).OrderBy(o => o.CreatedAt).ThenBy(o => MailCultures.FallbackOrder(o.Culture)).ThenBy(o => o.Culture, StringComparer.Ordinal).ToList();

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

        if (custom)
        {
            return overrides.FirstOrDefault() is { } any ? new MailTemplateContent(any.Subject, any.Body) : new MailTemplateContent(string.Empty, string.Empty);
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
        foreach (var (name, value) in Members(model))
        {
            result[name] = Convert(value, escape);
        }

        return result;
    }

    // dictionaries and JSON objects (models stored as JSON) are named like the properties of an object: OldValue is old_value
    private static IEnumerable<(string Name, object? Value)> Members(object model) => model switch
    {
        JsonElement json => json.EnumerateObject().Select(p => (StandardMemberRenamer.Rename(p.Name), (object?)p.Value)),
        IEnumerable<KeyValuePair<string, object?>> pairs => pairs.Select(p => (StandardMemberRenamer.Rename(p.Key), p.Value)),
        _ => model.GetType().GetProperties().Select(p => (StandardMemberRenamer.Default(p), p.GetValue(model))),
    };

    private static object? Convert(object? value, bool escape) => value switch
    {
        null => null,
        string text => escape ? WebUtility.HtmlEncode(text) : text,
        Uri uri => escape ? WebUtility.HtmlEncode(uri.ToString()) : uri.ToString(),
        _ when value.GetType().IsPrimitive || value is decimal or DateTime or DateTimeOffset or Guid or Enum => value,
        JsonElement json => Convert(json, escape),
        IEnumerable<KeyValuePair<string, object?>> => ToScript(value, escape),
        IEnumerable items => new ScriptArray(items.Cast<object?>().Select(item => Convert(item, escape))),
        _ => ToScript(value, escape),
    };

    private static object? Convert(JsonElement json, bool escape) => json.ValueKind switch
    {
        JsonValueKind.Object => ToScript(json, escape),
        JsonValueKind.Array => new ScriptArray(json.EnumerateArray().Select(item => Convert(item, escape))),
        JsonValueKind.String => Convert(json.GetString(), escape),
        JsonValueKind.Number => json.TryGetInt64(out var number) ? (object)number : json.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
