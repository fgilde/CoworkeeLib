using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

internal static class UserRoles
{
    public static async Task<Dictionary<Guid, List<RoleRefDto>>> ForAsync(CoworkeeDbContext db, List<Guid> userIds, CancellationToken cancellationToken)
    {
        var rows = await (from userRole in db.Set<IdentityUserRole<Guid>>()
                          join role in db.Set<Role>() on userRole.RoleId equals role.Id
                          where userIds.Contains(userRole.UserId)
                          select new { userRole.UserId, role.Id, role.Name }).ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.Select(r => new RoleRefDto(r.Id, r.Name!)).ToList());
    }

    public static UserDto ToDto(this User user, Dictionary<Guid, List<RoleRefDto>> roles) =>
        new(user.Id, user.UserName!, user.Email!, user.FirstName, user.LastName, user.IsActive, roles.GetValueOrDefault(user.Id) ?? []);
}
