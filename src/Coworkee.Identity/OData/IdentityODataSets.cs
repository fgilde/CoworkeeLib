using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.OData;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.OData;

/// <summary>Users, roles and groups as OData sets for the admin tables; exposed where the host loads <see cref="CoworkeeODataModule"/>.</summary>
internal static class IdentityODataSets
{
    public static void AddIdentityODataSets(this IServiceCollection services)
    {
        services.AddODataEntity<User>("Users", IdentityPermissions.Users.View,
            u => u.PasswordHash, u => u.SecurityStamp, u => u.ConcurrencyStamp, u => u.NormalizedEmail, u => u.NormalizedUserName);
        services.AddODataEntity<Role>("Roles", IdentityPermissions.Roles.View, r => r.NormalizedName, r => r.ConcurrencyStamp);
        services.AddODataEntity<UserGroup>("Groups", IdentityPermissions.Groups.View);
        services.AddScoped<IODataEntityFilter<User>, UserODataFilter>();
        services.AddScoped<IODataEntityFilter<Role>, RoleODataFilter>();
    }
}
