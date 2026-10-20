using Coworkee.Application.Calendar;
using Coworkee.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Coworkee.AspNetCore.Tests;

public sealed class IcsResultsTests
{
    [Fact]
    public async Task Calendar_is_served_as_utf8_text_calendar()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        var calendar = new IcsCalendar().Add(new IcsEvent("1", new DateOnly(2026, 1, 1), "Neujahr"));
        app.MapGet("/feed", () => Results.Ics(calendar));
        app.MapGet("/download", () => Results.Ics(calendar, "team.ics"));
        await app.StartAsync(ct);
        var client = app.GetTestClient();

        using var feed = await client.GetAsync("/feed", ct);
        feed.Content.Headers.ContentType!.MediaType.ShouldBe("text/calendar");
        feed.Content.Headers.ContentType.CharSet.ShouldBe("utf-8");
        feed.Content.Headers.ContentDisposition.ShouldBeNull();
        (await feed.Content.ReadAsStringAsync(ct)).ShouldContain("SUMMARY:Neujahr\r\n");

        using var download = await client.GetAsync("/download", ct);
        download.Content.Headers.ContentDisposition!.FileName.ShouldBe("team.ics");
    }
}
