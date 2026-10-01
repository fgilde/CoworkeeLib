using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ResourcePermissionsPanelTests : ClientTestBase
{
    private readonly Guid _folder = Guid.CreateVersion7();
    private readonly Guid _entry = Guid.CreateVersion7();
    private readonly Guid _user = Guid.CreateVersion7();
    private readonly Guid _role = Guid.CreateVersion7();

    public ResourcePermissionsPanelTests()
    {
        Api.GetResourcePermissionsAsync("Folder", _folder, Arg.Any<CancellationToken>()).Returns([new ResourcePermissionDto(_entry, PrincipalType.User, _user, _role)]);
        Api.GetRolesAsync(Arg.Any<CancellationToken>()).Returns([new RoleDto(_role, "Editor", null, false)]);
        Api.GetUsersAsync(Arg.Any<PageRequest>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<UserDto>([new UserDto(_user, "ann@acme.test", "ann@acme.test", "Ann", null, true, [])], 1, 1, 200));
        Api.GetGroupsAsync(Arg.Any<PageRequest>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<GroupDto>([], 0, 1, 200));
    }

    [Fact]
    public void Lists_entries_with_principal_and_role_names()
    {
        var panel = Render<ResourcePermissionsPanel>(p => p.Add(x => x.ResourceType, "Folder").Add(x => x.ResourceId, _folder));

        panel.WaitForAssertion(() => panel.Markup.ShouldContain("ann@acme.test"));
        panel.Markup.ShouldContain("Editor");
    }

    [Fact]
    public async Task Revoke_calls_the_api_for_that_entry()
    {
        var panel = Render<ResourcePermissionsPanel>(p => p.Add(x => x.ResourceType, "Folder").Add(x => x.ResourceId, _folder));

        await panel.WaitForElement($"[data-testid='revoke-{_entry}']").ClickAsync(new());

        await Api.Received(1).RevokeResourcePermissionAsync("Folder", _folder, _entry, Arg.Any<CancellationToken>());
    }
}
