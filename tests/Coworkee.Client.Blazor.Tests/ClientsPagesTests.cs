using Bunit;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
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

    private static void Type(IRenderedComponent<ChipListEditor> list, string text)
    {
        list.Find("input").Input(text);
        list.Find("input").Blur();
    }

    private (Task Editing, IReadOnlyList<IRenderedComponent<ChipListEditor>> Lists) EditApp()
    {
        var page = Render<Clients>();
        var edit = page.WaitForElement("[data-testid='edit-client-app']");
        var editing = edit.ClickAsync(new());
        _dialogs.WaitForAssertion(() => _dialogs.FindComponents<ChipListEditor>().Count.ShouldBe(3));
        return (editing, _dialogs.FindComponents<ChipListEditor>());
    }

    [Fact]
    public async Task Saving_a_client_sends_the_chips_as_lists()
    {
        var (editing, lists) = EditApp();

        Type(lists[1], "https://app.test/out");
        Type(lists[2], "email orders:read");
        await _dialogs.Find("form").SubmitAsync();
        await editing;

        await _clients.Received(1).UpdateClientAsync(AppId, Arg.Is<ClientRequest>(r =>
            r.RedirectUris.SequenceEqual(new[] { "https://app.test/cb" })
            && r.PostLogoutRedirectUris.SequenceEqual(new[] { "https://app.test/out" })
            && r.Scopes.SequenceEqual(new[] { "openid", "profile", "email", "orders:read" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_chip_lists_use_the_full_width_of_the_form()
    {
        var (editing, lists) = EditApp();

        foreach (var list in _dialogs.FindAll("[data-testid='chip-list']"))
        {
            list.Closest(".mud-grid-item")!.ClassList.ShouldBe(["mud-grid-item", "mud-grid-item-xs-12"], true);
        }

        await _dialogs.Find("form").SubmitAsync();
        await editing;
    }

    [Fact]
    public async Task Scope_suggestions_include_the_standard_and_the_defined_scopes()
    {
        var (editing, lists) = EditApp();
        var suggestions = lists[2].Instance.Suggestions!;

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

        Type(lists[1], "/out");
        Type(lists[2], "bad/scope");

        lists[1].FindAll("[data-chip]").ShouldBeEmpty();
        lists[1].Markup.ShouldContain("Not an absolute address");
        lists[2].FindAll("[data-chip='bad/scope']").ShouldBeEmpty();
        lists[2].Markup.ShouldContain("Letters, digits and . _ : - only");

        lists[1].Find("input").Input(string.Empty);
        lists[2].Find("input").Input(string.Empty);
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
        _dialogs.WaitForAssertion(() => _dialogs.FindComponents<ChipListEditor>().Count.ShouldBe(1));
        var resources = _dialogs.FindComponent<ChipListEditor>();

        resources.Find("[data-chip='https://orders.test']");
        (await resources.Instance.Suggestions!(string.Empty, Xunit.TestContext.Current.CancellationToken)).ShouldBe(["https://stock.test"]);
        Type(resources, "https://billing.test");
        await _dialogs.Find("form").SubmitAsync();
        await editing;

        await _clients.Received(1).UpdateScopeAsync(ScopeId, Arg.Is<ScopeRequest>(r =>
            r.Resources.SequenceEqual(new[] { "https://orders.test", "https://billing.test" })), Arg.Any<CancellationToken>());
    }
}
