using Coworkee.Identity.Users.Profile;
using FluentValidation;

namespace Coworkee.Identity.Users.Admin;

internal sealed class SetUserAvatarValidator : AbstractValidator<SetUserAvatar>
{
    public SetUserAvatarValidator() =>
        RuleFor(c => c.DataUrl)
            .MaximumLength(SetMyAvatarValidator.MaxLength).WithMessage("The picture may have at most about 300 KB.")
            .Must(url => url is null || AvatarData.TryParse(url, out _, out _)).WithMessage("The picture has to be a PNG, JPEG, GIF or WebP image.");
}
