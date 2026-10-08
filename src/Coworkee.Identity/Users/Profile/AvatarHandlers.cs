using System.Globalization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users.Profile;

internal sealed class AvatarHandlers(CoworkeeDbContext db, ICurrentUser currentUser, UserChanges changes, TimeProvider clock)
    : IHandler<SetMyAvatar, Result<ProfileDto>>,
      IHandler<GetUserAvatar, Result<UserAvatar>>,
      IHandler<GetUserCards, Result<IReadOnlyList<UserCardDto>>>
{
    private static readonly Error NoAvatar = Error.NotFound("users.no_avatar", "The user has no picture.");

    public async Task<Result<ProfileDto>> HandleAsync(SetMyAvatar command, CancellationToken cancellationToken)
    {
        if (await Colleagues().SingleOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        user.AvatarUrl = command.DataUrl;
        user.AvatarChangedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await changes.NotifyAsync(user, cancellationToken);
        return new ProfileDto(user.Email!, user.FirstName, user.LastName, user.PhoneNumber, user.AvatarUrl);
    }

    public async Task<Result<UserAvatar>> HandleAsync(GetUserAvatar query, CancellationToken cancellationToken) =>
        await Colleagues().Where(u => u.Id == query.UserId).Select(u => u.AvatarUrl).SingleOrDefaultAsync(cancellationToken) is { } url
        && AvatarData.TryParse(url, out var contentType, out var content)
            ? new UserAvatar(content, contentType)
            : NoAvatar;

    public async Task<Result<IReadOnlyList<UserCardDto>>> HandleAsync(GetUserCards query, CancellationToken cancellationToken)
    {
        var ids = query.Ids.Take(500).Distinct().ToList();
        var users = await Colleagues().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, HasAvatar = u.AvatarUrl != null, u.AvatarChangedAt })
            .ToListAsync(cancellationToken);
        return users.Select(u => new UserCardDto(
                u.Id,
                UserChanges.Name(u.FirstName, u.LastName, u.Email),
                u.HasAvatar ? (u.AvatarChangedAt ?? DateTimeOffset.MinValue).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) : null))
            .ToList();
    }

    private IQueryable<User> Colleagues() => db.Set<User>().Where(u => u.TenantId == currentUser.TenantId);
}
