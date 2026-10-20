using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Coworkee.Contracts.Reporting;
using Coworkee.Mailing;
using Coworkee.Reporting.Subscriptions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Reporting.Tests;

public sealed partial class SubscriptionTests(ReportingApp app) : ReportingTestBase(app)
{
    private static readonly ReportSchedule Weekly = new(SubscriptionFrequency.Weekly, new TimeOnly(7, 0), "Europe/Berlin", DayOfWeek.Monday);

    private static SaveReportRequest Report(string name = "Sales by mail") => new(name, null, null, ReportVisibility.Organisation, [], new ReportDefinition
    {
        Widgets =
        [
            new ReportWidget { Id = "count", Type = WidgetType.Kpi, Title = "Sales", Query = new WidgetQuery { EntitySet = "Sales" },
                Options = new WidgetOptions { Thresholds = [new(0, ReportThresholds.Error, "Few"), new(10, ReportThresholds.Success, "Plenty")] } },
            new ReportWidget { Id = "regions", Type = WidgetType.Chart, Title = "Regions", Y = 3, Query = new WidgetQuery { EntitySet = "Sales", GroupBy = new ReportGroupBy("Region") } },
            new ReportWidget { Id = "rows", Type = WidgetType.Table, Title = "Rows", Y = 8, Query = new WidgetQuery { EntitySet = "Sales", Columns = ["Region", "Amount"], OrderBy = "Amount", Descending = true } },
        ],
    });

    private async Task<ReportDto> CreateReportAsync(SaveReportRequest? request = null)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/reports", request ?? Report(), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReportDto>(Ct))!;
    }

    private static async Task<ReportSubscriptionDto> SubscribeAsync(HttpClient client, Guid reportId, ReportSchedule? schedule = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/reports/subscriptions", new SaveReportSubscriptionRequest(reportId, schedule ?? Weekly), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ReportSubscriptionDto>(Ct))!;
    }

    private async Task RunDueAsync(params Guid[] ids)
    {
        await App.InDbAsync(null, db => db.Set<ReportSubscription>().IgnoreQueryFilters().Where(s => ids.Contains(s.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.NextRunAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));
        await using var scope = App.App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportSubscriptionJob>().ExecuteAsync(Ct);
    }

    private async Task<List<(OutgoingMail Mail, List<OutgoingMailAttachment> Files)>> MailsAsync()
    {
        var result = new List<(OutgoingMail, List<OutgoingMailAttachment>)>();
        await App.InDbAsync(null, async db =>
        {
            foreach (var mail in await db.Set<OutgoingMail>().IgnoreQueryFilters().Where(m => m.TemplateName == ReportSubscriptionMail.Template).ToListAsync(Ct))
            {
                result.Add((mail, await db.Set<OutgoingMailAttachment>().Where(a => a.MailId == mail.Id).ToListAsync(Ct)));
            }
        });
        return result;
    }

    private Task<ReportSubscription> StoredAsync(Guid id)
    {
        ReportSubscription? stored = null;
        return App.InDbAsync(null, async db => stored = await db.Set<ReportSubscription>().IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == id, Ct)).ContinueWith(_ => stored!, Ct);
    }

    [Theory]
    [InlineData("Daily", null, null, "07:00", "Europe/Berlin", "2026-10-20T06:00:00+00:00", "2026-10-21T07:00:00+02:00")]
    [InlineData("Daily", null, null, "07:00", "Europe/Berlin", "2026-10-20T04:00:00+00:00", "2026-10-20T07:00:00+02:00")]
    [InlineData("Weekly", "Monday", null, "09:00", "UTC", "2026-10-21T10:00:00+00:00", "2026-10-26T09:00:00+00:00")]
    [InlineData("Monthly", null, 31, "08:00", "UTC", "2027-02-01T00:00:00+00:00", "2027-02-28T08:00:00+00:00")]
    [InlineData("Monthly", null, 31, "08:00", "UTC", "2027-02-28T09:00:00+00:00", "2027-03-31T08:00:00+00:00")]
    [InlineData("Daily", null, null, "02:30", "Europe/Berlin", "2026-03-28T12:00:00+00:00", "2026-03-29T03:00:00+02:00")]
    [InlineData("Daily", null, null, "02:30", "Europe/Berlin", "2026-10-24T12:00:00+00:00", "2026-10-25T02:30:00+02:00")]
    [InlineData("Daily", null, null, "07:00", "Europe/Berlin", "2026-10-25T12:00:00+00:00", "2026-10-26T07:00:00+01:00")]
    public void The_next_mail_follows_the_schedule_in_its_time_zone(string frequency, string? weekday, int? day, string time, string zone, string after, string expected)
    {
        var schedule = new ReportSchedule(Enum.Parse<SubscriptionFrequency>(frequency), TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture), zone,
            weekday is null ? null : Enum.Parse<DayOfWeek>(weekday), day);

        var next = ReportSchedules.Next(schedule, DateTimeOffset.Parse(after, System.Globalization.CultureInfo.InvariantCulture));

        next.ShouldBe(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
        next.Offset.ShouldBe(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture).Offset);
    }

    [Fact]
    public async Task The_mail_shows_what_the_subscriber_may_see_with_an_excel_file()
    {
        var report = await CreateReportAsync();
        var south = await CreateUserAsync("south@acme.test", ReportPermissions.View, ReportingApp.ViewSales);
        SouthOnlyFilter.Users[south.Id] = true;
        var subscription = await SubscribeAsync(App.As(south.Id, Setup.TenantId), report.Id);

        await RunDueAsync(subscription.Id);

        var (mail, files) = (await MailsAsync()).ShouldHaveSingleItem();
        mail.To.ShouldBe("south@acme.test");
        var southCount = Visible.Count(s => s.Region == "South");
        mail.HtmlBody.ShouldContain($"✓ {southCount}");
        mail.HtmlBody.ShouldContain("Plenty");
        mail.HtmlBody.ShouldContain("https://app.test/reports/" + report.Id);
        mail.HtmlBody.ShouldNotContain("North");
        var file = files.ShouldHaveSingleItem();
        file.FileName.ShouldBe("Sales by mail.xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        workbook.Worksheets.Select(w => w.Name).ShouldBe(["Sales", "Regions", "Rows"]);
        workbook.Worksheet("Sales").Cell(2, 1).GetDouble().ShouldBe(southCount);
        workbook.Worksheet("Rows").RowsUsed().Count().ShouldBe(southCount + 1);
        var stored = await StoredAsync(subscription.Id);
        stored.LastSentAt.ShouldNotBeNull();
        stored.LastError.ShouldBeNull();
        stored.NextRunAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_report_that_is_no_longer_shared_sends_nothing_and_says_why()
    {
        var report = await CreateReportAsync();
        var reader = await CreateUserAsync("reader@acme.test", ReportPermissions.View, ReportingApp.ViewSales);
        var subscription = await SubscribeAsync(App.As(reader.Id, Setup.TenantId), report.Id);
        (await Admin.PutAsJsonAsync($"/api/v1/reports/{report.Id}", Report() with { Visibility = ReportVisibility.Private }, Ct)).EnsureSuccessStatusCode();

        await RunDueAsync(subscription.Id);

        (await MailsAsync()).ShouldBeEmpty();
        (await StoredAsync(subscription.Id)).LastError.ShouldBe("The report is no longer shared with the subscriber.");
    }

    [Fact]
    public async Task The_unsubscribe_link_asks_first_and_then_ends_the_subscription()
    {
        var report = await CreateReportAsync();
        var subscription = await SubscribeAsync(Admin, report.Id);
        await RunDueAsync(subscription.Id);
        var link = UnsubscribeLink().Match((await MailsAsync()).Single().Mail.HtmlBody).Groups[1].Value.Replace("&amp;", "&", StringComparison.Ordinal);
        var token = Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);
        var anonymous = App.App.GetTestClient();

        (await anonymous.GetAsync(new Uri(link).PathAndQuery, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StoredAsync(subscription.Id)).ShouldNotBeNull();
        (await anonymous.PostAsync(ReportMailLinks.Route, new FormUrlEncodedContent([new("token", "forged")]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await anonymous.PostAsync(ReportMailLinks.Route, new FormUrlEncodedContent([new("token", token)]), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Admin.GetFromJsonAsync<List<ReportSubscriptionDto>>("/api/v1/reports/subscriptions", Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Users_keep_their_own_subscriptions_and_the_overview_needs_its_permission()
    {
        var report = await CreateReportAsync();
        var reader = App.As((await CreateUserAsync("reader2@acme.test", ReportPermissions.View)).Id, Setup.TenantId);
        var mine = await SubscribeAsync(reader, report.Id, new ReportSchedule(SubscriptionFrequency.Monthly, new TimeOnly(6, 0), "UTC", DayOfMonth: 1));
        await SubscribeAsync(Admin, report.Id);

        (await reader.GetFromJsonAsync<List<ReportSubscriptionDto>>("/api/v1/reports/subscriptions", Ct))!.ShouldHaveSingleItem().Id.ShouldBe(mine.Id);
        (await reader.GetAsync("/api/v1/reports/subscriptions/all", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.GetFromJsonAsync<List<ReportSubscriptionDto>>("/api/v1/reports/subscriptions/all", Ct))!.Count.ShouldBe(2);
        (await reader.PutAsJsonAsync($"/api/v1/reports/subscriptions/{mine.Id}", new SaveReportSubscriptionRequest(report.Id, Weekly, new ReportDateRange(DateRangePreset.LastMonth)), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        mine.NextRunAt.UtcDateTime.Day.ShouldBe(1);
        (await Admin.DeleteAsync($"/api/v1/reports/subscriptions/{mine.Id}", Ct)).IsSuccessStatusCode.ShouldBeTrue();
        (await reader.GetFromJsonAsync<List<ReportSubscriptionDto>>("/api/v1/reports/subscriptions", Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_an_own_language_the_mail_and_the_unsubscribe_page_take_the_default_language()
    {
        var language = App.App.Services.GetRequiredService<TestDefaultLanguage>();
        language.Culture = "de";
        try
        {
            var report = await CreateReportAsync();
            var subscription = await SubscribeAsync(Admin, report.Id);

            await RunDueAsync(subscription.Id);

            var body = (await MailsAsync()).Single().Mail.HtmlBody;
            body.ShouldContain("hier ist der Bericht");
            var link = UnsubscribeLink().Match(body).Groups[1].Value.Replace("&amp;", "&", StringComparison.Ordinal);
            (await (await App.App.GetTestClient().GetAsync(new Uri(link).PathAndQuery, Ct)).Content.ReadAsStringAsync(Ct)).ShouldContain("lang=\"de\"");
        }
        finally
        {
            language.Culture = null;
        }
    }

    [Fact]
    public async Task A_time_series_shows_its_latest_buckets_in_chronological_order()
    {
        var report = await CreateReportAsync(new SaveReportRequest("Shipped by month", null, null, ReportVisibility.Organisation, [], new ReportDefinition
        {
            Widgets =
            [
                new ReportWidget { Id = "months", Type = WidgetType.Chart, Title = "Months",
                    Query = new WidgetQuery { EntitySet = "Sales", GroupBy = new ReportGroupBy("ShippedOn", DateBucket.Month), Descending = true } },
            ],
        }));
        var subscription = await SubscribeAsync(Admin, report.Id);
        var months = Visible.Where(s => s.ShippedOn is not null).Select(s => $"{s.ShippedOn!.Value.Year}-{s.ShippedOn.Value.Month:00}").Distinct().Order(StringComparer.Ordinal).ToList();

        await RunDueAsync(subscription.Id);

        var body = (await MailsAsync()).Single().Mail.HtmlBody;
        months.Count.ShouldBeGreaterThan(ReportSubscriptionMail.MailRows);
        body.ShouldNotContain($">{months[^(ReportSubscriptionMail.MailRows + 1)]}<");
        var shown = months.TakeLast(ReportSubscriptionMail.MailRows).Select(m => body.IndexOf($">{m}<", StringComparison.Ordinal)).ToList();
        shown.ShouldAllBe(i => i >= 0);
        shown.ShouldBe(shown.Order());
        body.ShouldContain("Only the latest rows are shown");
    }

    [Theory]
    [InlineData("Mars/Olympus", "Weekly", "TimeZone")]
    [InlineData("UTC", "WeeklyWithoutDay", "Weekday")]
    public async Task Unknown_time_zones_and_missing_weekdays_are_refused(string zone, string kind, string key)
    {
        var report = await CreateReportAsync();
        var schedule = new ReportSchedule(SubscriptionFrequency.Weekly, new TimeOnly(7, 0), zone, kind == "Weekly" ? DayOfWeek.Monday : null);

        var response = await Admin.PostAsJsonAsync("/api/v1/reports/subscriptions", new SaveReportSubscriptionRequest(report.Id, schedule), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain(key);
    }

    [GeneratedRegex("href=\"([^\"]*/api/v1/reports/subscriptions/unsubscribe\\?token=[^\"]*)\"")]
    private static partial Regex UnsubscribeLink();
}
