using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class PermissionPickerTests : ClientTestBase
{
    private IReadOnlyList<string> _value = [];
    private PermissionMatch _match;

    public PermissionPickerTests()
    {
        Api.GetPermissionDefinitionsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new PermissionGroupDto("Identity", "Identity",
            [
                new PermissionDto("Identity.Users.View", "View users", []),
                new PermissionDto("Identity.Users.Manage", "Manage users", ["Identity.Users.View"]),
            ]),
            new PermissionGroupDto("Mail", "Mail", [new PermissionDto("Mail.Templates.Manage", "Manage mail templates", [])]),
        ]);
    }

    private IRenderedComponent<PermissionPicker> RenderPicker(IReadOnlyList<string> value, bool showMatch = true) =>
        Render<PermissionPicker>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, v => _value = v)
            .Add(x => x.ShowMatch, showMatch)
            .Add(x => x.MatchChanged, m => _match = m));

    [Fact]
    public void Shows_the_permissions_grouped_with_their_display_names_and_the_search_filters_them()
    {
        var picker = RenderPicker([]);

        picker.WaitForAssertion(() => picker.FindAll("[data-picker-group]").Count.ShouldBe(2));
        picker.Find("[data-picker-permission='Identity.Users.View']").TextContent.ShouldContain("View users");

        picker.Find("[data-testid='picker-search'] input, input[data-testid='picker-search']").Input("templates");

        picker.FindAll("[data-picker-group]").Select(g => g.GetAttribute("data-picker-group")).ShouldBe(["Mail"]);
        picker.FindAll("[data-picker-permission]").Count.ShouldBe(1);
    }

    [Fact]
    public async Task Selecting_and_removing_permissions_updates_the_value_and_the_match_shows_from_two_on()
    {
        var picker = RenderPicker([]);
        picker.WaitForElement("[data-picker-permission='Identity.Users.View'] input").Change(true);

        _value.ShouldBe(["Identity.Users.View"]);
        picker.Find("[data-picker-chip='Identity.Users.View']").TextContent.ShouldContain("View users");
        picker.FindAll("[data-testid='picker-match']").ShouldBeEmpty();

        picker.Find("[data-picker-permission='Mail.Templates.Manage'] input").Change(true);

        _value.ShouldBe(["Identity.Users.View", "Mail.Templates.Manage"]);
        await picker.Find("[data-testid='picker-match'] [data-match='All']").ClickAsync(new());
        _match.ShouldBe(PermissionMatch.All);

        await picker.Find("[data-picker-chip='Identity.Users.View'] .mud-chip-close-button").ClickAsync(new());

        _value.ShouldBe(["Mail.Templates.Manage"]);
        picker.FindAll("[data-picker-chip]").Count.ShouldBe(1);
        picker.FindAll("[data-testid='picker-match']").ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_selected_permissions_stay_visible_by_name_and_the_match_can_be_left_out()
    {
        var picker = RenderPicker(["Custom.Gone", "Identity.Users.View"], showMatch: false);

        picker.WaitForAssertion(() => picker.Find("[data-picker-chip='Custom.Gone']").TextContent.ShouldContain("Custom.Gone"));
        picker.FindAll("[data-testid='picker-match']").ShouldBeEmpty();
    }
}
