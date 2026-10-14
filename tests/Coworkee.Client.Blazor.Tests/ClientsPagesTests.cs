using Bunit;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions.Components;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ClientsPagesTests : ClientTestBase
{
    private static readonly Guid AppId = Guid.CreateVersion7();
    private static readonly Guid ScopeId = Guid.CreateVersion7();
    private readonly IClientsApi _clients = Substitute.For<IClientsApi>();
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;

    public ClientsPagesTests()
    {
        Services.AddSingleton(_clients);
        AddAuthorization().SetAuthorized("Ada").SetPolicies(PermissionPolicy.For(IdentityPermissions.Clients.Manage));
        Api.GetRolesAsync(default).ReturnsForAnyArgs([]);
        Api.GetPermissionDefinitionsAsync(default).ReturnsForAnyArgs([]);
        _clients.GetClientsAsync(default).ReturnsForAnyArgs([App(["profile"])]);
        _clients.GetScopesAsync(default).ReturnsForAnyArgs([new ScopeDto(ScopeId, "orders:read", null, null, ["https://orders.test"])]);
        _clients.UpdateClientAsync(default, default!, default).ReturnsForAnyArgs(new ClientSecretDto(AppId, null));
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
    }

    private static ClientDto App(string[] scopes) =>
        new(AppId, "app", "App", "public", "implicit", ["https://app.test/cb"], [], [ClientGrantTypes.AuthorizationCode], scopes, false, [], []);

    private static async Task Type(IRenderedComponent<MudExTagField<string>> list, string text)
    {
        await list.Find("input").InputAsync(new ChangeEventArgs { Value = text });
        await list.Find("input").BlurAsync(new FocusEventArgs());
    }

    private (Task Editing, IReadOnlyList<IRenderedComponent<MudExTagField<string>>> Lists) EditApp()
    {
        var page = Render<Clients>();
        var edit = page.WaitForElement("[data-testid='edit-client-app']");
        var editing = edit.ClickAsync(new());
        _dialogs.WaitForAssertion(() => _dialogs.FindComponents<MudExTagField<string>>().Count.ShouldBe(3));
        return (editing, _dialogs.FindComponents<MudExTagField<string>>());
    }

    [Fact]
    public async Task Saving_a_client_sends_the_chips_as_lists()
    {
        var (editing, lists) = EditApp();

        await Type(lists[1], "https://app.test/out");
        await Type(lists[2], "email orders:read");
        await _dialogs.Find("form").SubmitAsync();
        await editing;

        await _clients.Received(1).UpdateClientAsync(AppId, Arg.Is<ClientRequest>(r =>
            r.RedirectUris.SequenceEqual(new[] { "https://app.test/cb" })
            && r.PostLogoutRedirectUris.SequenceEqual(new[] { "https://app.test/out" })
            && r.Scopes.SequenceEqual(new[] { "openid", "profile", "email", "orders:read" })), Arg.Any<CancellationToken>());
    }

    private Task Duplicate(string clientId)
    {
        var page = Render<Clients>();
        var duplicating = page.WaitForElement($"[data-testid='duplicate-client-{clientId}']").ClickAsync(new());
        _dialogs.WaitForElement("form");
        return duplicating;
    }

    [Fact]
    public async Task Duplicating_an_application_creates_a_copy_with_a_free_id_and_the_settings_of_the_original()
    {
        _clients.GetClientsAsync(default).ReturnsForAnyArgs([App(["profile"]), App(["profile"]) with { Id = Guid.CreateVersion7(), ClientId = "app-copy" }]);
        _clients.CreateClientAsync(default!, default).ReturnsForAnyArgs(new ClientSecretDto(Guid.CreateVersion7(), null));

        var duplicating = Duplicate("app");
        await _dialogs.Find("form").SubmitAsync();
        await duplicating;

        await _clients.Received(1).CreateClientAsync(Arg.Is<ClientRequest>(r =>
            r.ClientId == "app-copy-2" && r.DisplayName == "App (copy)" && r.ClientType == "public"
            && r.RedirectUris.SequenceEqual(new[] { "https://app.test/cb" }) && r.Scopes.SequenceEqual(new[] { "openid", "profile" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Duplicating_a_configured_confidential_application_shows_the_new_secret()
    {
        _clients.GetClientsAsync(default).ReturnsForAnyArgs([App(["profile"]) with { ClientId = "svc", ClientType = "confidential", Managed = true }]);
        _clients.CreateClientAsync(default!, default).ReturnsForAnyArgs(new ClientSecretDto(Guid.CreateVersion7(), "s3cret"));

        var duplicating = Duplicate("svc");
        await _dialogs.Find("form").SubmitAsync();

        _dialogs.WaitForAssertion(() => _dialogs.Find("[data-testid='client-secret']").TextContent.ShouldBe("s3cret"));
        await _clients.Received(1).CreateClientAsync(Arg.Is<ClientRequest>(r => r.ClientId == "svc-copy" && r.ClientType == "confidential"), Arg.Any<CancellationToken>());
        duplicating.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task The_chip_lists_use_the_full_width_of_the_form()
    {
        var (editing, lists) = EditApp();

        foreach (var list in lists)
        {
            _dialogs.Find($"label[for='{list.Find("label").GetAttribute("for")}']").Closest(".mud-grid-item")!.ClassList.ShouldBe(["mud-grid-item", "mud-grid-item-xs-12"], true);
        }

        await _dialogs.Find("form").SubmitAsync();
        await editing;
    }

    [Fact]
    public async Task Scope_suggestions_include_the_standard_and_the_defined_scopes()
    {
        var (editing, lists) = EditApp();
        var suggestions = lists[2].Instance.SearchFunc!;

        var all = (await suggestions(string.Empty, Xunit.TestContext.Current.CancellationToken)).ToList();

        all.ShouldBe(["openid", "profile", "email", "roles", "offline_access", "phone", "address", "orders:read"]);
        (await suggestions("ord", Xunit.TestContext.Current.CancellationToken)).ShouldBe(["orders:read"]);

        await _dialogs.Find("form").SubmitAsync();
        await editing;
    }

    [Fact]
    public async Task openid_is_always_part_of_the_scopes_and_cannot_be_removed()
    {
        var (editing, lists) = EditApp();

        lists[2].Find("[data-chip='openid']");
        lists[2].FindAll("[data-chip='openid'] .mud-chip-close-button").ShouldBeEmpty();
        lists[2].FindAll("[data-chip='profile'] .mud-chip-close-button").Count.ShouldBe(1);

        await _dialogs.Find("form").SubmitAsync();
        await editing;
    }

    [Fact]
    public async Task Relative_addresses_and_scopes_with_special_characters_are_rejected()
    {
        var (editing, lists) = EditApp();

        await Type(lists[1], "/out");
        await Type(lists[2], "bad/scope");

        lists[1].WaitForAssertion(() => lists[1].Markup.ShouldContain("Not an absolute address"));
        lists[1].FindAll("[data-chip]").ShouldBeEmpty();
        lists[2].WaitForAssertion(() => lists[2].Markup.ShouldContain("Letters, digits and . _ : - only"));
        lists[2].FindAll("[data-chip='bad/scope']").ShouldBeEmpty();

        await lists[1].Find("input").InputAsync(new ChangeEventArgs { Value = string.Empty });
        await lists[2].Find("input").InputAsync(new ChangeEventArgs { Value = string.Empty });
        await _dialogs.Find("form").SubmitAsync();
        await editing;
    }

    [Fact]
    public async Task Resources_of_a_scope_are_chips_with_the_resources_of_the_other_scopes_as_suggestions()
    {
        _clients.GetScopesAsync(Xunit.TestContext.Current.CancellationToken).ReturnsForAnyArgs([
            new ScopeDto(ScopeId, "orders:read", null, null, ["https://orders.test"]),
            new ScopeDto(Guid.CreateVersion7(), "stock:read", null, null, ["https://stock.test"])]);
        var page = Render<Scopes>();
        var editing = page.WaitForElements("[aria-label='Edit']")[0].ClickAsync(new());
        _dialogs.WaitForAssertion(() => _dialogs.FindComponents<MudExTagField<string>>().Count.ShouldBe(1));
        var resources = _dialogs.FindComponent<MudExTagField<string>>();

        resources.Find("[data-chip='https://orders.test']");
        (await resources.Instance.SearchFunc!(string.Empty, Xunit.TestContext.Current.CancellationToken)).ShouldBe(["https://stock.test"]);
        await Type(resources, "https://billing.test");
        await _dialogs.Find("form").SubmitAsync();
        await editing;

        await _clients.Received(1).UpdateScopeAsync(ScopeId, Arg.Is<ScopeRequest>(r =>
            r.Resources.SequenceEqual(new[] { "https://orders.test", "https://billing.test" })), Arg.Any<CancellationToken>());
    }
}
