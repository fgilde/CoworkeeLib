namespace Coworkee.Contracts.Mailing;

public sealed record MailTemplateDto(
    string Name, string Culture, string Subject, string Body, string DefaultSubject, string DefaultBody, bool IsOverridden, IReadOnlyList<MailVariableDto> Variables, bool IsCustom)
{
    /// <summary>The template's sample model as a JSON object with the names the template uses, the start of a send for real.</summary>
    public string? SampleModel { get; init; }
}
