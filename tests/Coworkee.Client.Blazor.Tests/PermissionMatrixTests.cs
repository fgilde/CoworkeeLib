using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Identity;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class PermissionMatrixTests : ClientTestBase
{
    private readonly Guid _role = Guid.CreateVersion7();

    public PermissionMatrixTests()
    {
        Api.GetPermissionDefinitionsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new PermissionGroupDto("Identity", "Identity",
            [
                new PermissionDto("Identity.Users.View", "View users", []),
                new PermissionDto("Identity.Users.Manage", "Manage users", ["Identity.Users.View"]),
            ]),
        ]);
        Api.GetGrantsAsync(PermissionProviderType.Role, _role, Arg.Any<CancellationToken>()).Returns(["Identity.Users.View"]);
    }

    [Fact]
    public void Shows_current_grants_as_checked()
    {
        var matrix = Render<PermissionMatrix>(p => p.Add(m => m.ProviderType, PermissionProviderType.Role).Add(m => m.ProviderKey, _role));

        matrix.WaitForAssertion(() => matrix.Find("[data-permission='Identity.Users.View'] input").HasAttribute("checked").ShouldBeTrue());
        matrix.Find("[data-permission='Identity.Users.Manage'] input").HasAttribute("checked").ShouldBeFalse();
    }

    [Fact]
    public async Task Saves_exactly_the_checked_permissions()
    {
        var matrix = Render<PermissionMatrix>(p => p.Add(m => m.ProviderType, PermissionProviderType.Role).Add(m => m.ProviderKey, _role));
        matrix.WaitForElement("[data-permission='Identity.Users.Manage'] input").Change(true);
        matrix.Find("[data-permission='Identity.Users.View'] input").Change(false);

        await matrix.Find("[data-testid='save-permissions']").ClickAsync(new());

        await Api.Received(1).SetGrantsAsync(PermissionProviderType.Role, _role,
            Arg.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "Identity.Users.Manage" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Read_only_matrix_has_no_save_button()
    {
        var matrix = Render<PermissionMatrix>(p => p.Add(m => m.ProviderType, PermissionProviderType.Role).Add(m => m.ProviderKey, _role).Add(m => m.ReadOnly, true));

        matrix.WaitForElement("[data-permission='Identity.Users.View']");
        matrix.FindAll("[data-testid='save-permissions']").ShouldBeEmpty();
    }
}
