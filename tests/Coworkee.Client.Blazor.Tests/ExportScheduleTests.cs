using Bunit;
using Coworkee.Client.Blazor.Components.Exports;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Reporting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ExportScheduleTests : ClientTestBase
{
    private readonly ExportScheduleDto _schedule = new(Guid.CreateVersion7(), "Gadgets", "Tools weekly", "?$filter=Category eq 'Tools'&format=csv",
        new ReportSchedule(SubscriptionFrequency.Monthly, new TimeOnly(7, 0), "UTC", DayOfWeek.Friday, null, 2, MonthWeek.Last), null, ["boss@acme.test"],
        DateTimeOffset.UtcNow.AddDays(3), null, null);

    public ExportScheduleTests()
    {
        AddAuthorization().SetAuthorized("Ada").SetPolicies(Security.PermissionPolicy.For(DataTransferPermissions.ExportSchedules));
        Transfer.GetExportSchedulesAsync(null, Arg.Any<CancellationToken>()).Returns([_schedule]);
        Transfer.RunExportScheduleAsync(_schedule.Id, Arg.Any<CancellationToken>()).Returns(_schedule with { LastRunAt = DateTimeOffset.UtcNow });
    }

    [Fact]
    public async Task The_page_lists_the_schedules_in_words_and_sends_one_now()
    {
        Render<MudPopoverProvider>();
        var page = Render<Pages.ExportSchedules>();

        page.WaitForAssertion(() => page.Markup.ShouldContain("Monthly on the last Friday at "));
        page.Markup.ShouldContain(", every 2 months (UTC)");
        page.Markup.ShouldContain("boss@acme.test");
        await page.InvokeAsync(() => page.Find("[data-testid='export-schedule-run']").ClickAsync(new()));

        await Transfer.Received(1).RunExportScheduleAsync(_schedule.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_dialog_keeps_the_list_query_and_swaps_only_the_format()
    {
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var showing = dialogs.InvokeAsync(() => ExportScheduleDialog.ShowAsync(Services.GetRequiredService<IDialogService>(), "Schedule export", "Gadgets", _schedule.Query, null, _schedule));
        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='export-schedule-next']"));

        await dialogs.Find("[data-testid='export-schedule-save']").ClickAsync(new());
        var request = (await showing)!;

        request.Query.ShouldBe("?$filter=Category eq 'Tools'&format=csv");
        (request.Schedule.Every, request.Schedule.Week, request.Schedule.Weekday, request.Schedule.DayOfMonth).ShouldBe((2, MonthWeek.Last, DayOfWeek.Friday, (int?)null));
        request.Recipients.ShouldBe(["boss@acme.test"]);
        ExportScheduleDialog.WithFormat("?columns=Name&format=csv", ExportFileFormat.Xlsx).ShouldBe("?columns=Name&format=xlsx");
    }
}
