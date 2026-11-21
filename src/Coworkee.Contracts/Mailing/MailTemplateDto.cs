namespace Coworkee.Contracts.Mailing;

public sealed record MailTemplateDto(
    string Name, string Culture, string Subject, string Body, string DefaultSubject, string DefaultBody, bool IsOverridden, IReadOnlyList<MailVariableDto> Variables, bool IsCustom)
{
    /// <summary>The template's sample model as a JSON object with the names the template uses, the start of a send for real.</summary>
    public string? SampleModel { get; init; }

    /// <summary>
    /// <see cref="SampleModel"/> cut down to the values the template's texts (every language) read, without those the send fills per recipient
    /// (<c>user</c>, <c>unsubscribe_url</c>): what a send for real asks for; <c>{}</c> when the template reads nothing of its model.
    /// </summary>
    public string? SendModel { get; init; }
}
