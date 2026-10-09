using Bunit;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Identity;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class RoleDetailTests : ClientTestBase
{
    private readonly Guid _role = Guid.CreateVersion7();

    public RoleDetailTests()
    {
        Api.GetRolesAsync(Arg.Any<CancellationToken>()).Returns([new RoleDto(_role, "Customer", "Buys things", false)]);
        Api.GetPermissionDefinitionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        Api.GetGrantsAsync(PermissionProviderType.Role, _role, Arg.Any<CancellationToken>()).Returns([]);
    }

    [Fact]
    public void Managers_offer_a_role_in_the_registration()
    {
        AddAuthorization().SetAuthorized("Ada").SetPolicies(Security.PermissionPolicy.For(IdentityPermissions.Roles.Manage));
        var page = Render<RoleDetail>(p => p.Add(r => r.Id, _role));

        page.WaitForElement("input[data-testid='role-registration'], [data-testid='role-registration'] input").Change(true);

        Api.Received().UpdateRoleAsync(_role, new RoleRequest("Customer", "Buys things", true), Arg.Any<CancellationToken>());
    }
}
