using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Identity.Users.Profile;

/// <summary>Sets or (with null) removes the own profile picture.</summary>
[AiTool(Exclude = true)]
public sealed record SetMyAvatar(string? DataUrl) : ICommand<Result<ProfileDto>>;

/// <summary>The profile picture of a user of the own organisation.</summary>
public sealed record GetUserAvatar(Guid UserId) : IQuery<Result<UserAvatar>>;

public sealed record UserAvatar(byte[] Content, string ContentType);

/// <summary>Names and avatar versions of users of the own organisation, e.g. for user cards in lists and chats.</summary>
public sealed record GetUserCards(IReadOnlyList<Guid> Ids) : IQuery<Result<IReadOnlyList<UserCardDto>>>;

internal sealed class SetMyAvatarValidator : AbstractValidator<SetMyAvatar>
{
    public const int MaxLength = 400_000;

    public SetMyAvatarValidator() =>
        RuleFor(c => c.DataUrl)
            .MaximumLength(MaxLength).WithMessage("The picture may have at most about 300 KB.")
            .Must(url => url is null || AvatarData.TryParse(url, out _, out _)).WithMessage("The picture has to be a PNG, JPEG, GIF or WebP image.");
}
