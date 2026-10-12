using Bunit;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Pages;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Identity;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ApiKeyPagesTests : ClientTestBase
{
    private static readonly Guid AdaId = Guid.CreateVersion7();
    private readonly IApiKeysApi _keys = Substitute.For<IApiKeysApi>();
    private readonly FakeODataClient _odata = new();

    public ApiKeyPagesTests()
    {
        Services.AddSingleton(_keys);
        Services.AddSingleton<IODataClient>(_odata);
        AddAuthorization().SetAuthorized("Ada").SetClaims(new System.Security.Claims.Claim("sub", AdaId.ToString()))
            .SetPolicies(PermissionPolicy.For(ApiKeyPermissions.Create), PermissionPolicy.For(ApiKeyPermissions.Manage));
        Render<MudPopoverProvider>();
    }

    [Fact]
    public async Task A_new_key_offers_only_the_users_permissions_and_shows_its_secret_once_with_the_mcp_setup()
    {
        _keys.GetMineAsync(default).ReturnsForAnyArgs([]);
        _keys.GetScopesAsync(default).ReturnsForAnyArgs([new PermissionGroupDto("Ai", "AI", [new PermissionDto(AiPermissions.Chat, "Use the assistant and the MCP server", [])]),
            new PermissionGroupDto("Notes", "Notes", [new PermissionDto("Notes.Read", "Read notes", []), new PermissionDto("Notes.Write", "Write notes", [])])]);
        _keys.CreateAsync(default!, default).ReturnsForAnyArgs(call => new CreatedApiKeyDto(Key("Claude Desktop") with { Scopes = call.Arg<CreateApiKeyRequest>().Scopes }, "cwk_" + new string('a', 64)));
        var dialogs = Render<MudDialogProvider>();
        var page = Render<ProfileApiKeys>();
        page.WaitForAssertion(() => page.Markup.ShouldContain("no API keys yet"));

        var creating = page.Find("[data-testid='api-key-new']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.FindAll("[data-permission]").Count.ShouldBe(3));
        dialogs.Find("input[data-testid='api-key-name'], [data-testid='api-key-name'] input").Change("Claude Desktop");
        await dialogs.Find($"[data-permission='{AiPermissions.Chat}'] input").ChangeAsync(new() { Value = true });
        await dialogs.Find("[data-permission='Notes.Write'] input").ChangeAsync(new() { Value = true });
        _keys.GetMineAsync(default).ReturnsForAnyArgs([Key("Claude Desktop")]);
        await dialogs.Find("[data-testid='api-key-create']").ClickAsync(new());
        await creating;

        page.WaitForAssertion(() => page.Find("[data-testid='api-key-secret']").TextContent.ShouldBe("cwk_" + new string('a', 64)));
        page.Find("[data-testid='mcp-url']").TextContent.ShouldEndWith("/mcp");
        page.Find("[data-testid='mcp-config']").TextContent.ShouldContain("\"Authorization\": \"Bearer cwk_");
        await _keys.Received(1).CreateAsync(Arg.Is<CreateApiKeyRequest>(r => r.Name == "Claude Desktop" && r.Scopes.SequenceEqual(new[] { AiPermissions.Chat, "Notes.Write" })
            && r.ExpiresAt > DateTimeOffset.UtcNow.AddDays(89)), Arg.Any<CancellationToken>());
        page.Find("[data-key='Claude Desktop'] [data-status='active']").ShouldNotBeNull();

        // once closed, the secret is gone for good
        page.Find("[data-testid='api-key-created'] .mud-alert-close button, [data-testid='api-key-created'] button.mud-alert-close").Click();
        page.FindAll("[data-testid='api-key-secret']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Revoking_asks_first_and_stops_the_key()
    {
        var key = Key("Script");
        _keys.GetMineAsync(default).ReturnsForAnyArgs([key]);
        var dialogs = Render<MudDialogProvider>();
        var page = Render<ProfileApiKeys>();
        page.WaitForAssertion(() => page.Find("[data-testid='api-key-revoke']"));

        _keys.GetMineAsync(default).ReturnsForAnyArgs([key with { RevokedAt = DateTimeOffset.UtcNow }]);
        var revoking = page.Find("[data-testid='api-key-revoke']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Revoke").ShouldBeTrue());
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Revoke").ClickAsync(new());
        await revoking;

        await _keys.Received(1).RevokeAsync(key.Id, Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => page.Find("[data-status='revoked']"));
        page.FindAll("[data-testid='api-key-revoke']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Administrators_see_the_keys_of_everyone_with_their_owners_and_revoke_them()
    {
        var bob = Guid.CreateVersion7();
        _odata.With("ApiKeys", Key("Claude Desktop"), Key("Nightly import") with { UserId = bob, ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) });
        Api.GetUserNamesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, string> { [AdaId] = "Ada Lovelace", [bob] = "Bob Builder" });
        var dialogs = Render<MudDialogProvider>();

        var page = Render<Pages.Admin.ApiKeys>();

        page.WaitForAssertion(() => page.Markup.ShouldContain("Bob Builder"));
        page.FindAll("[data-status='expired']").Count.ShouldBe(1);
        var revoking = page.Find("[data-testid='revoke-Nightly import']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain("Bob Builder"));
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Revoke").ClickAsync(new());
        await revoking;
        await _keys.ReceivedWithAnyArgs(1).RevokeAsync(default);
    }

    private static ApiKeyDto Key(string name) =>
        new(Guid.CreateVersion7(), AdaId, name, null, "cwk_abc123", [AiPermissions.Chat], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), null, null, null);
}
