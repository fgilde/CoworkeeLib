namespace Coworkee.Mailing;

public interface IMailTemplateDefinitionManager
{
    IReadOnlyList<MailTemplateDefinition> All { get; }

    MailTemplateDefinition? Find(string name);
}
