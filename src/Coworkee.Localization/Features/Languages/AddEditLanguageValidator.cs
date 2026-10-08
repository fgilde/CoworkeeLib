using System.Globalization;
using FluentValidation;

namespace Coworkee.Localization.Features.Languages;

internal sealed class AddEditLanguageValidator : AbstractValidator<AddEditLanguageCommand>
{
    public AddEditLanguageValidator()
    {
        RuleFor(c => c.Language.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Language.Culture).NotEmpty().MaximumLength(20).Must(BeACulture).WithMessage("Unknown culture.");
    }

    private static bool BeACulture(string culture)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(culture) && CultureInfo.GetCultureInfo(culture, predefinedOnly: true) is not null;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
