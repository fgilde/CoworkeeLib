namespace Coworkee.Mailing;

public interface IMailTemplateCatalog
{
    Task<MailTemplateDefinition?> FindAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<MailTemplateDefinition>> AllAsync(CancellationToken cancellationToken);

    bool IsCustom(MailTemplateDefinition definition);
}
