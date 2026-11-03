namespace Coworkee.Contracts.Mailing;

public sealed record MailTemplateDto(
    string Name, string Culture, string Subject, string Body, string DefaultSubject, string DefaultBody, bool IsOverridden, IReadOnlyList<MailVariableDto> Variables, bool IsCustom);
