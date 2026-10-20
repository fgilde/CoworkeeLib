using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Reporting.Pages;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Coworkee.Client.Blazor.Reporting.Tests;

public sealed class AiReportTests : ReportingTestBase
{
    private static readonly Guid Id = Guid.NewGuid();

    private static ReportDefinition Generated => new()
    {
        Widgets =
        [
            new ReportWidget { Id = "a", Type = WidgetType.Markdown, Options = new() { Text = "AI one" } },
            new ReportWidget { Id = "b", Type = WidgetType.Markdown, Y = 3, Options = new() { Text = "AI two" } },
        ],
    };

    public AiReportTests()
    {
        Services.GetRequiredService<ICoworkeeApi>().GetMyPermissionsAsync(default).ReturnsForAnyArgs([ReportPermissions.View, ReportPermissions.Manage]);
        Auth.SetPolicies(Security.PermissionPolicy.For(ReportPermissions.View), Security.PermissionPolicy.For(ReportPermissions.Manage));
        Reports.IsAiAvailableAsync(default).ReturnsForAnyArgs(true);
        Reports.GetReportsAsync(default).ReturnsForAnyArgs([]);
    }

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void Create_with_ai_offers_examples_from_the_sources_and_opens_the_result_unsaved()
    {
        Reports.GenerateAsync(default!, default).ReturnsForAnyArgs(Generated);
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        var dialogs = Render<MudBlazor.MudDialogProvider>();
        var list = Render<Reports>();

        list.WaitForElement("[data-testid='report-create-ai']").Click();
        var examples = dialogs.WaitForElements("[data-testid='report-ai-example']");
        examples.Count.ShouldBe(1);
        examples[0].TextContent.ShouldContain("Sales orders");
        examples[0].Click();
        dialogs.Find("[data-testid='report-ai-generate']").Click();

        list.WaitForAssertion(() => Nav.Uri.ShouldEndWith("reports/new"));
        Reports.Received().GenerateAsync(Arg.Is<GenerateReportRequest>(r => r.Prompt.Contains("Sales orders") && r.Current == null), Arg.Any<CancellationToken>());
        Services.GetRequiredService<ReportDrafts>().Take()!.Definition.Widgets.Count.ShouldBe(2);
        Reports.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public void A_draft_opens_in_the_designer_with_undo_and_is_created_on_save()
    {
        Services.GetRequiredService<ReportDrafts>().Set(new SaveReportRequest("Draft", null, null, ReportVisibility.Private, [], Generated));
        Reports.CreateAsync(default!, default).ReturnsForAnyArgs(new ReportDto(Id, "Draft", null, null, ReportVisibility.Private, [], "Ada", true, false, Generated, DateTimeOffset.UtcNow));
        Render<MudBlazor.MudPopoverProvider>();
        var page = Render<ReportPage>();

        page.WaitForAssertion(() => page.FindAll(".grid-stack-item").Count.ShouldBe(2));
        page.Find("[data-testid='undo']").HasAttribute("disabled").ShouldBeFalse();
        page.Find("[data-testid='report-save']").Click();

        page.WaitForAssertion(() => Nav.Uri.ShouldEndWith($"reports/{Id}"));
        Reports.Received().CreateAsync(Arg.Is<SaveReportRequest>(r => r.Name == "Draft" && r.Definition.Widgets.Count == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Asking_ai_to_change_a_report_replaces_its_widgets_and_undo_brings_them_back()
    {
        var report = new ReportDto(Id, "Sales", null, null, ReportVisibility.Private, [], "Ada", true, false,
            new ReportDefinition { Widgets = [new ReportWidget { Id = "k", Type = WidgetType.Markdown, Options = new() { Text = "Mine" } }] }, DateTimeOffset.UtcNow);
        Reports.GetReportAsync(Id, default).ReturnsForAnyArgs(report);
        Reports.GenerateAsync(default!, default).ReturnsForAnyArgs(Generated);
        Nav.NavigateTo($"reports/{Id}?edit=true");
        Render<MudBlazor.MudPopoverProvider>();
        var dialogs = Render<MudBlazor.MudDialogProvider>();
        var page = Render<ReportPage>(p => p.Add(x => x.Id, Id));

        page.WaitForElement("[data-testid='report-ai-change']").Click();
        dialogs.WaitForElement("[data-testid='report-ai-prompt']").Input("Two notes instead");
        dialogs.Find("[data-testid='report-ai-generate']").Click();

        page.WaitForAssertion(() => page.FindAll(".grid-stack-item").Count.ShouldBe(2));
        Reports.Received().GenerateAsync(Arg.Is<GenerateReportRequest>(r => r.Current!.Widgets.Single().Id == "k"), Arg.Any<CancellationToken>());
        page.Find("[data-testid='undo']").Click();
        page.WaitForAssertion(() => page.FindAll(".grid-stack-item").Count.ShouldBe(1));
        Reports.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default);
    }

    [Fact]
    public void Errors_of_the_server_stay_in_the_dialog()
    {
        Reports.GenerateAsync(default!, default).ThrowsAsyncForAnyArgs(new ApiException(400, "validation",
            new Dictionary<string, string[]> { ["Prompt"] = ["The AI model gave no usable report after 3 tries: Sales has no field 'Nope'."] }));
        Render<MudBlazor.MudPopoverProvider>();
        var dialogs = Render<MudBlazor.MudDialogProvider>();
        var list = Render<Reports>();

        list.WaitForElement("[data-testid='report-create-ai']").Click();
        dialogs.WaitForElement("[data-testid='report-ai-prompt']").Input("Something");
        dialogs.Find("[data-testid='report-ai-generate']").Click();

        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='report-ai-error']").TextContent.ShouldContain("no field 'Nope'"));
        Nav.Uri.ShouldNotEndWith("reports/new");
    }
}
