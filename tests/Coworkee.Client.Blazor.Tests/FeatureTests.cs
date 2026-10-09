using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Features;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class FeatureTests : ClientTestBase
{
    private static readonly IReadOnlyList<FeatureGroupDto> Groups =
        [new FeatureGroupDto("Reports", "Reports", [new FeatureDefinitionDto("Reports.Enabled", "Reports", null, FeatureType.Bool, "false")])];

    private readonly FakeODataClient _odata = new();

    public FeatureTests()
    {
        Services.AddSingleton<IODataClient>(_odata);
        AddAuthorization().SetAuthorized("Ada").SetPolicies(
            Security.PermissionPolicy.For(FeaturePermissions.Tenants), Security.PermissionPolicy.For(FeaturePermissions.Editions));
        FeaturesApi.GetDefinitionsAsync(Arg.Any<CancellationToken>()).Returns(Groups);
    }

    private void Features(params IReadOnlyDictionary<string, string?>[] answers) =>
        FeaturesApi.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(answers[0], answers[1..]);

    [Fact]
    public void The_gate_follows_the_feature_and_reloads_on_change()
    {
        Features(new Dictionary<string, string?> { ["Reports.Enabled"] = "false" }, new Dictionary<string, string?> { ["Reports.Enabled"] = "true" });

        var gate = Render<FeatureGate>(p => p
            .Add(g => g.Feature, "Reports.Enabled")
            .Add(g => g.ChildContent, (RenderFragment)(b => b.AddContent(0, "reports")))
            .Add(g => g.Disabled, (RenderFragment)(b => b.AddContent(0, "upgrade"))));

        gate.WaitForAssertion(() => gate.Markup.ShouldContain("upgrade"));
        Services.GetRequiredService<FakeRealtimeConnection>().Push(FeatureTopics.Changed);

        gate.WaitForAssertion(() => gate.Markup.ShouldContain("reports"));
    }

    [Fact]
    public async Task Navigation_hides_items_of_disabled_features()
    {
        Features(new Dictionary<string, string?> { ["Reports.Enabled"] = "false", ["Board.Enabled"] = "true" });
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new Contracts.Identity.SetupStatusDto(true));
        Services.AddSingleton<INavigationContributor>(new LayoutCustomizationTests.StaticNavigation(
            new CoworkeeNavItem("Reports", "/reports", "", Feature: "Reports.Enabled"),
            new CoworkeeNavItem("Board", "/board", "", Feature: "Board.Enabled")));

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));
        await OpenNavigationAsync(layout);

        layout.WaitForAssertion(() => layout.Markup.ShouldContain("/board"));
        layout.Markup.ShouldNotContain("/reports");
    }

    [Fact]
    public async Task Tenants_show_users_and_edition_and_assign_another_edition()
    {
        var tenant = new TenantRow(Guid.CreateVersion7(), "Acme", "acme", true, false, false);
        var basic = new EditionDto(Guid.CreateVersion7(), "Basic", null, new Dictionary<string, string>(), 1);
        var pro = new EditionDto(Guid.CreateVersion7(), "Pro", null, new Dictionary<string, string> { ["Reports.Enabled"] = "true" }, 0);
        _odata.With("Tenants", tenant);
        FeaturesApi.GetEditionsAsync(Arg.Any<CancellationToken>()).Returns([basic, pro]);
        FeaturesApi.GetTenantDetailsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new TenantDetailsDto(tenant.Id, basic.Id, new Dictionary<string, string>(), 7)]);

        Render<MudPopoverProvider>();
        var page = Render<Tenants>();

        page.WaitForAssertion(() => page.Find("[data-tenant='acme']").ShouldNotBeNull());
        page.WaitForAssertion(() => page.Markup.ShouldContain("Basic"));
        page.Find(".mud-table-body").TextContent.ShouldContain("7");

        var select = page.FindComponent<MudSelect<Guid?>>();
        await page.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(pro.Id));

        page.WaitForAssertion(() => FeaturesApi.Received(1).SetTenantFeaturesAsync(tenant.Id, Arg.Is<TenantFeaturesRequest>(r => r.EditionId == pro.Id), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public async Task Edition_feature_values_are_saved_from_the_side_sheet()
    {
        var pro = new EditionDto(Guid.CreateVersion7(), "Pro", null, new Dictionary<string, string>(), 0);
        FeaturesApi.GetEditionsAsync(Arg.Any<CancellationToken>()).Returns([pro]);
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var page = Render<Editions>();
        page.WaitForAssertion(() => page.Markup.ShouldContain("Pro"));

        var editing = page.Find("[data-testid='edition-features-Pro']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.Find("[data-feature='Reports.Enabled']"));
        var select = dialogs.FindComponent<MudSelect<string>>();
        await dialogs.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync("true"));
        await dialogs.Find("[data-testid='save-features']").ClickAsync(new());
        await editing;

        await FeaturesApi.Received(1).UpdateEditionAsync(pro.Id,
            Arg.Is<EditionRequest>(r => r.Values!["Reports.Enabled"] == "true"), Arg.Any<CancellationToken>());
    }
}
