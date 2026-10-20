using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Reporting.Pages;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Reporting.Tests;

public sealed class SubscriptionTests : ReportingTestBase
{
    private static readonly Guid Id = Guid.NewGuid();

    private static readonly ReportSubscriptionDto Mine = new(Guid.NewGuid(), Id, "Sales", Guid.NewGuid(), "Ada",
        new ReportSchedule(SubscriptionFrequency.Weekly, new TimeOnly(7, 0), "UTC", DayOfWeek.Monday), null, null, true, DateTimeOffset.UtcNow.AddDays(2), null, "The report is no longer shared with the subscriber.");

    [Fact]
    public void Subscribing_from_the_report_sends_the_schedule_and_the_chosen_filters()
    {
        var filters = new List<ReportFilterValue> { new("SalesOrders", "Region", "Region", "North", "Region eq 'North'") };
        Reports.GetReportAsync(Id, default).ReturnsForAnyArgs(new ReportDto(Id, "Sales", null, null, ReportVisibility.Organisation, [], "Ada", false, false,
            new ReportDefinition { Filters = filters }, DateTimeOffset.UtcNow));
        Reports.SubscribeAsync(default!, default).ReturnsForAnyArgs(Mine);
        Render<MudBlazor.MudPopoverProvider>();
        var dialogs = Render<MudBlazor.MudDialogProvider>();
        var page = Render<ReportPage>(p => p.Add(x => x.Id, Id));

        page.WaitForElement("[data-testid='report-subscribe']").Click();
        dialogs.WaitForElement("[data-testid='subscription-next']").TextContent.ShouldNotBeEmpty();
        dialogs.Find("[data-testid='subscription-save']").Click();

        page.WaitForAssertion(() => Reports.Received().SubscribeAsync(Arg.Is<SaveReportSubscriptionRequest>(r =>
            r.ReportId == Id && r.Schedule.Frequency == SubscriptionFrequency.Weekly && r.Schedule.Weekday == DayOfWeek.Monday && r.Schedule.Time == new TimeOnly(7, 0)
            && r.Filters!.Single().OData == "Region eq 'North'" && r.Attachment), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void My_subscriptions_show_the_schedule_and_the_last_error_and_end_on_request()
    {
        var loads = 0;
        Reports.GetSubscriptionsAsync(false, default).ReturnsForAnyArgs(_ => loads++ == 0 ? [Mine] : Array.Empty<ReportSubscriptionDto>());
        Render<MudBlazor.MudPopoverProvider>();
        var dialogs = Render<MudBlazor.MudDialogProvider>();
        var page = Render<ReportSubscriptions>();

        page.WaitForAssertion(() => page.Find("[data-testid='subscriptions']").TextContent.ShouldContain("07:00"));
        page.Find("[data-testid='subscription-error']");
        page.FindAll("[data-testid='subscriptions-all']").ShouldBeEmpty();
        page.Find("[data-testid='subscription-delete']").Click();
        dialogs.WaitForElement(".mud-dialog .mud-button-filled").Click();

        page.WaitForAssertion(() => Reports.Received().DeleteSubscriptionAsync(Mine.Id, Arg.Any<CancellationToken>()));
        page.WaitForElement("[data-testid='subscriptions-empty']");
    }

    [Fact]
    public void The_overview_of_the_organisation_needs_its_permission()
    {
        Services.GetRequiredService<ICoworkeeApi>().GetMyPermissionsAsync(default).ReturnsForAnyArgs([ReportPermissions.View, ReportPermissions.Subscriptions]);
        Reports.GetSubscriptionsAsync(default, default).ReturnsForAnyArgs([Mine]);
        var page = Render<ReportSubscriptions>();

        page.WaitForElement("[data-testid='subscriptions-all']").Click();

        page.WaitForAssertion(() => Reports.Received().GetSubscriptionsAsync(true, Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => page.Find("[data-testid='subscriptions']").TextContent.ShouldContain("Ada"));
    }
}
