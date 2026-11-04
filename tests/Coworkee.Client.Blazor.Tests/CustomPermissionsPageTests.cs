using Bunit;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class CustomPermissionsPageTests : ClientTestBase
{
    private const string Export = "Custom.Reports.Export";
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;

    public CustomPermissionsPageTests()
    {
        AddAuthorization().SetAuthorized("Ada").SetPolicies(PermissionPolicy.For(IdentityPermissions.Permissions.Manage));
        Api.GetCustomPermissionsAsync(default).ReturnsForAnyArgs([new CustomPermissionDto(Export, "Export reports", "Shows the export button", "Reports")]);
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
    }

    [Fact]
    public void The_page_lists_the_custom_permissions_with_their_texts_and_groups()
    {
        var page = Render<CustomPermissions>();

        page.WaitForAssertion(() => page.Find("[data-testid='custom-permissions']").TextContent.ShouldContain(Export));
        var text = page.Find("[data-testid='custom-permissions']").TextContent;
        text.ShouldContain("Export reports");
        text.ShouldContain("Shows the export button");
        text.ShouldContain("Reports");
    }

    [Fact]
    public async Task Adding_sends_the_name_and_the_texts()
    {
        var page = Render<CustomPermissions>();
        var adding = page.WaitForElement("[data-testid='create-custom-permission']").ClickAsync(new());
        var form = _dialogs.WaitForElement("form");

        await InputByLabel(form, "Name").ChangeAsync(new() { Value = "Custom.Header.Help" });
        await InputByLabel(form, "Display name").ChangeAsync(new() { Value = "Help button" });
        await _dialogs.Find("form").SubmitAsync();
        await adding;

        await Api.Received(1).CreateCustomPermissionAsync(
            Arg.Is<CreateCustomPermissionRequest>(r => r.Name == "Custom.Header.Help" && r.DisplayName == "Help button"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deleting_warns_with_the_roles_groups_and_users_that_lose_the_permission()
    {
        Api.GetCustomPermissionUsageAsync(Export, default).ReturnsForAnyArgs(new CustomPermissionUsageDto(["Exporters", "Managers (Beta)"], ["Sales"], ["eve@acme.test"]));
        var page = Render<CustomPermissions>();

        var deleting = page.WaitForElement($"[data-testid='delete-{Export}']").ClickAsync(new());
        var dialog = _dialogs.WaitForElement("[data-testid='custom-permission-delete']");

        var usage = dialog.TextContent;
        usage.ShouldContain("Exporters");
        usage.ShouldContain("Managers (Beta)");
        usage.ShouldContain("Sales");
        usage.ShouldContain("eve@acme.test");
        usage.ShouldContain("lose this permission");
        await Api.DidNotReceiveWithAnyArgs().DeleteCustomPermissionAsync(default!, default);

        await _dialogs.Find("[data-testid='custom-permission-delete-confirm']").ClickAsync(new());
        await deleting;

        await Api.Received(1).DeleteCustomPermissionAsync(Export, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelling_the_delete_keeps_the_permission()
    {
        Api.GetCustomPermissionUsageAsync(Export, default).ReturnsForAnyArgs(new CustomPermissionUsageDto([], [], []));
        var page = Render<CustomPermissions>();

        var deleting = page.WaitForElement($"[data-testid='delete-{Export}']").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid='custom-permission-delete']").TextContent.ShouldContain("No role, group or user holds it.");
        await _dialogs.Find("[data-testid='custom-permission-delete-cancel']").ClickAsync(new());
        await deleting;

        await Api.DidNotReceiveWithAnyArgs().DeleteCustomPermissionAsync(default!, default);
    }
}
