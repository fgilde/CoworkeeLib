using System.Globalization;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;

namespace Coworkee.Mailing;

internal static class MailErrors
{
    public static readonly Error UnknownTemplate = Error.NotFound("mail.template_not_found", "The mail template does not exist.");

    public static Error? CheckCulture(string culture)
    {
        try
        {
            _ = CultureInfo.GetCultureInfo(culture, predefinedOnly: true);
            return null;
        }
        catch (CultureNotFoundException)
        {
            return Error.Validation("Culture", $"Unknown culture '{culture}'.");
        }
    }

    public static Error? CheckSyntax(SaveMailTemplateRequest content) =>
        MailTemplateValidation.Validate(content.Subject, content.Body) is { Count: > 0 } errors
            ? Error.Validation("Body", string.Join(Environment.NewLine, errors))
            : null;
}
