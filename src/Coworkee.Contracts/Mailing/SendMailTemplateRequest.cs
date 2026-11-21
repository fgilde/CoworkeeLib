using Coworkee.Contracts.Identity;

namespace Coworkee.Contracts.Mailing;

/// <summary>
/// A template sent for real, one mail per recipient of <see cref="Recipients"/> in his language. <see cref="Model"/> is the template's model as a JSON object
/// (null: the template's sample, <see cref="MailTemplateDto.SampleModel"/>); per recipient <c>user</c> gets his <c>first_name</c>, <c>last_name</c>,
/// <c>display_name</c>, <c>email</c> and <c>culture</c>, and an <c>unsubscribe_url</c> key his own opt-out link.
/// </summary>
public sealed record SendMailTemplateRequest(UserSelection Recipients, string? Model = null);

/// <summary>Who gets the mail (<see cref="Excluded"/>: chosen, but inactive, locked, without address or opted out) and the mail of one recipient (the first by default).</summary>
public sealed record MailTemplateSendPreviewDto(int Recipients, int Excluded, RenderedMailDto? Mail)
{
    /// <summary>Whose mail <see cref="Mail"/> is: "Ada Lovelace &lt;ada@example.com&gt;".</summary>
    public string? Recipient { get; init; }
}
