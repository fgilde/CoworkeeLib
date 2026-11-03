using FluentValidation;

namespace Coworkee.Notifications;

internal sealed class SaveNotificationPreferenceValidator : AbstractValidator<SaveNotificationPreference>
{
    public SaveNotificationPreferenceValidator() => RuleFor(c => c.Preference.Scope).IsInEnum();
}
