using System.Globalization;
using FluentValidation;

namespace Coworkee.Account.Users;

internal sealed class SetUserLanguageValidator : AbstractValidator<SetUserLanguage>
{
    public SetUserLanguageValidator() =>
        RuleFor(c => c.Culture).MaximumLength(20).Must(BeACulture).WithMessage("Unknown language.").When(c => c.Culture is not null);

    private static bool BeACulture(string? culture)
    {
        try
        {
            return CultureInfo.GetCultureInfo(culture!, predefinedOnly: true).Name.Length > 0;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
