namespace Coworkee.Mailing;

public interface IMailTemplateRenderer
{
    Task<RenderedMail> RenderAsync(string name, object model, string? culture, CancellationToken cancellationToken);

    Task<RenderedMail> RenderContentAsync(MailTemplateContent content, object model, string? culture, CancellationToken cancellationToken);

    /// <summary>Renders with an already resolved layout, for senders that render many mails of one template.</summary>
    Task<RenderedMail> RenderContentAsync(MailTemplateContent content, MailTemplateContent layout, object model, CancellationToken cancellationToken);

    Task<MailTemplateContent> ResolveAsync(string name, string? culture, CancellationToken cancellationToken);
}
