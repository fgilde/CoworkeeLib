using System.Reflection;
using System.Text.RegularExpressions;

namespace Coworkee.Mailing;

public sealed record MailTemplateContent(string Subject, string Body);

public sealed record MailTemplateDefinition(string Name, string DisplayName, object SampleModel, IReadOnlyDictionary<string, MailTemplateContent> Defaults);

public interface IMailTemplateContributor
{
    void Define(MailTemplateContext context);
}

public sealed class MailTemplateContext
{
    internal List<MailTemplateDefinition> Definitions { get; } = [];

    public MailTemplateContext Add(MailTemplateDefinition definition)
    {
        if (!definition.Defaults.ContainsKey(MailCultures.Fallback))
        {
            throw new ArgumentException($"Mail template '{definition.Name}' needs an '{MailCultures.Fallback}' default.", nameof(definition));
        }

        Definitions.RemoveAll(d => d.Name == definition.Name);
        Definitions.Add(definition);
        return this;
    }
}

public interface IMailTemplateDefinitionManager
{
    IReadOnlyList<MailTemplateDefinition> All { get; }

    MailTemplateDefinition? Find(string name);
}

internal sealed class MailTemplateDefinitionManager : IMailTemplateDefinitionManager
{
    public MailTemplateDefinitionManager(IEnumerable<IMailTemplateContributor> contributors)
    {
        var context = new MailTemplateContext();
        foreach (var contributor in contributors)
        {
            contributor.Define(context);
        }

        All = context.Definitions;
    }

    public IReadOnlyList<MailTemplateDefinition> All { get; }

    public MailTemplateDefinition? Find(string name) => All.FirstOrDefault(d => d.Name == name);
}

public static class MailCultures
{
    public const string Fallback = "en";

    public static IReadOnlyList<string> Candidates(string? culture)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(culture))
        {
            candidates.Add(culture);
            if (culture.IndexOf('-', StringComparison.Ordinal) is > 0 and var dash)
            {
                candidates.Add(culture[..dash]);
            }
        }

        candidates.Add(Fallback);
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public static partial class EmbeddedTemplates
{
    public static IReadOnlyDictionary<string, MailTemplateContent> Load(Assembly assembly, string name)
    {
        var defaults = new Dictionary<string, MailTemplateContent>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            var match = ResourceName().Match(resource);
            if (!match.Success || match.Groups["name"].Value != name || match.Groups["part"].Value != "body")
            {
                continue;
            }

            var culture = match.Groups["culture"].Value;
            var subjectResource = resource[..^"body.scriban".Length] + "subject.scriban";
            defaults[culture] = new MailTemplateContent(
                assembly.GetManifestResourceNames().Contains(subjectResource) ? Read(assembly, subjectResource).Trim() : string.Empty,
                Read(assembly, resource));
        }

        return defaults.Count > 0 ? defaults : throw new InvalidOperationException($"No embedded mail template '{name}' in {assembly.GetName().Name}.");
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"\.Templates\.(?<name>.+)\.(?<culture>[a-z]{2}(-[A-Z]{2})?)\.(?<part>subject|body)\.scriban$")]
    private static partial Regex ResourceName();
}

internal sealed class CoreMailTemplates : IMailTemplateContributor
{
    public const string Layout = "Layout";

    private static readonly object User = new { first_name = "Ada", last_name = "Lovelace", email = "ada@example.com" };

    public void Define(MailTemplateContext context)
    {
        var assembly = typeof(CoreMailTemplates).Assembly;
        context.Add(new(Layout, "Layout", new { subject = "Subject", content = "<p>Content</p>" }, EmbeddedTemplates.Load(assembly, Layout)));
        context.Add(new("Identity.ConfirmEmail", "Confirm email", new { user = User, confirm_url = "https://example.com/confirm" }, EmbeddedTemplates.Load(assembly, "Identity.ConfirmEmail")));
        context.Add(new("Identity.ResetPassword", "Reset password", new { user = User, reset_url = "https://example.com/reset" }, EmbeddedTemplates.Load(assembly, "Identity.ResetPassword")));
        context.Add(new("Identity.RegistrationPending", "Registration pending", new { user = User }, EmbeddedTemplates.Load(assembly, "Identity.RegistrationPending")));
        context.Add(new("Identity.RegistrationApproved", "Registration approved", new { user = User, login_url = "https://example.com" }, EmbeddedTemplates.Load(assembly, "Identity.RegistrationApproved")));
        context.Add(new("Identity.Welcome", "Welcome", new { user = User, login_url = "https://example.com" }, EmbeddedTemplates.Load(assembly, "Identity.Welcome")));
        context.Add(new("Identity.Invitation", "Invitation", new { user = User, invitation_url = "https://example.com/reset" }, EmbeddedTemplates.Load(assembly, "Identity.Invitation")));
        context.Add(new("Identity.ChangeEmail", "Confirm new email", new { user = User, new_email = "ada@example.org", confirm_url = "https://example.com/confirm" },
            EmbeddedTemplates.Load(assembly, "Identity.ChangeEmail")));
        context.Add(new("Identity.EmailChangeNotice", "Email change notice", new { user = User, new_email = "ada@example.org", pending = true },
            EmbeddedTemplates.Load(assembly, "Identity.EmailChangeNotice")));
        context.Add(new("Notifications.Digest", "Notification digest", new
        {
            user = User,
            notifications = new[] { new { title = "Document shared", body = "Grace shared a document with you.", link = "https://example.com/d/1" } },
        }, EmbeddedTemplates.Load(assembly, "Notifications.Digest")));
    }
}
