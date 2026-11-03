namespace Coworkee.Mailing;

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
