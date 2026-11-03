using FluentValidation;

namespace Coworkee.Identity.Users.Profile;

internal sealed class SetMyAvatarValidator : AbstractValidator<SetMyAvatar>
{
    public const int MaxLength = 400_000;

    public SetMyAvatarValidator() =>
        RuleFor(c => c.DataUrl)
            .MaximumLength(MaxLength).WithMessage("The picture may have at most about 300 KB.")
            .Must(url => url is null || AvatarData.TryParse(url, out _, out _)).WithMessage("The picture has to be a PNG, JPEG, GIF or WebP image.");
}
