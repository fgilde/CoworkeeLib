using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

internal sealed class MyProfileHandlers(CoworkeeDbContext db, ICurrentUser currentUser, Profile.UserChanges changes)
    : IHandler<GetMyProfile, Result<ProfileDto>>, IHandler<UpdateMyProfile, Result<ProfileDto>>
{
    public async Task<Result<ProfileDto>> HandleAsync(GetMyProfile query, CancellationToken cancellationToken) =>
        await MeAsync(cancellationToken) is { } user ? await MapAsync(db, user, cancellationToken) : UserErrors.NotFound;

    public async Task<Result<ProfileDto>> HandleAsync(UpdateMyProfile command, CancellationToken cancellationToken)
    {
        if (await MeAsync(cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        user.FirstName = Clean(command.Profile.FirstName);
        user.LastName = Clean(command.Profile.LastName);
        user.PhoneNumber = Clean(command.Profile.PhoneNumber);
        user.Street = Clean(command.Profile.Address?.Street);
        user.ZipCode = Clean(command.Profile.Address?.ZipCode);
        user.City = Clean(command.Profile.Address?.City);
        user.Country = Clean(command.Profile.Address?.Country);
        await db.SaveChangesAsync(cancellationToken);
        await changes.NotifyAsync(user, cancellationToken);
        return await MapAsync(db, user, cancellationToken);
    }

    private Task<User?> MeAsync(CancellationToken cancellationToken) =>
        db.Set<User>().SingleOrDefaultAsync(u => u.Id == currentUser.UserId && u.TenantId == currentUser.TenantId, cancellationToken);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static async Task<ProfileDto> MapAsync(CoworkeeDbContext db, User user, CancellationToken cancellationToken) =>
        new(user.Email!, user.FirstName, user.LastName, user.PhoneNumber, user.AvatarUrl, Address(user), user.PasswordHash is not null,
            await PasskeyCountAsync(db, user.Id, cancellationToken));

    internal static Task<int> PasskeyCountAsync(CoworkeeDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.Set<IdentityUserPasskey<Guid>>().CountAsync(p => p.UserId == userId, cancellationToken);

    internal static PostalAddress? Address(User user) =>
        (user.Street ?? user.ZipCode ?? user.City ?? user.Country) is null ? null : new PostalAddress(user.Street, user.ZipCode, user.City, user.Country);
}
