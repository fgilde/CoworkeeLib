namespace Coworkee.Mailing;

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
        context.Add(new("Notifications.Notification", "Notification", new
        {
            user = User,
            notification = new { title = "Document shared", body = "Grace shared a document with you.", link = "https://example.com/d/1" },
        }, EmbeddedTemplates.Load(assembly, "Notifications.Notification")));
        context.Add(new(ExportMailer.Template, "Export", new { user = User, export = new { name = "Products", file_name = "Products.xlsx" }, attached = true },
            EmbeddedTemplates.Load(assembly, ExportMailer.Template)));
    }
}
