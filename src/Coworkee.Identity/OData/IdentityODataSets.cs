using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Groups;
using Coworkee.OData;
using Coworkee.OData.GlobalSearch;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.OData;

/// <summary>Users, roles and groups as OData sets for the admin tables; exposed where the host loads <see cref="CoworkeeODataModule"/>.</summary>
internal static class IdentityODataSets
{
    public static void AddIdentityODataSets(this IServiceCollection services)
    {
        services.AddODataEntity<User>("Users", IdentityPermissions.Users.View,
            u => u.PasswordHash, u => u.PasswordHistory, u => u.KeptSession, u => u.SecurityStamp, u => u.ConcurrencyStamp, u => u.NormalizedEmail, u => u.NormalizedUserName);
        services.AddODataEntity<Role>("Roles", IdentityPermissions.Roles.View, r => r.NormalizedName, r => r.ConcurrencyStamp);
        services.AddODataEntity<UserGroup>("Groups", IdentityPermissions.Groups.View);
        services.AddODataImport<GroupImportRow, Result<Guid>>("Groups", row => new ImportGroup(null, row));
        services.AddODataEntity<ApiKeys.ApiKey>("ApiKeys", ApiKeyPermissions.Manage, k => k.SecretHash);
        services.AddScoped<IODataEntityFilter<User>, UserODataFilter>();
        services.AddScoped<IODataEntityFilter<Role>, RoleODataFilter>();
        services.AddGlobalSearch<User>(s => s.Fields(u => u.UserName, u => u.Email, u => u.FirstName, u => u.LastName)
            .Title(u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } name ? name : u.UserName).Subtitle(u => u.Email)
            .Url(u => $"/admin/users/{u.Id}").Thumbnail(u => u.AvatarUrl).Icon("Person"));
        services.AddGlobalSearch<Role>(s => s.Fields(r => r.Name, r => r.Description).Subtitle(r => r.Description).Url(r => $"/admin/roles/{r.Id}").Icon("Shield"));
        services.AddGlobalSearch<UserGroup>(s => s.Fields(g => g.Name, g => g.Description).Subtitle(g => g.Description).Url(g => $"/admin/groups?group={g.Id}").Icon("Groups"));
    }
}
