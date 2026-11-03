namespace Coworkee.Mailing;

public sealed record MailTemplateDefinition(string Name, string DisplayName, object SampleModel, IReadOnlyDictionary<string, MailTemplateContent> Defaults);
