namespace Coworkee.Mailing;

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
