using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coworkee.Application.Privacy;
using Coworkee.Application.Retention;
using Coworkee.Contracts.Auditing;
using Coworkee.Contracts.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Auditing.Tests;

public sealed class RequestLogTests(AuditApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private static string Marker() => Guid.NewGuid().ToString("N");

    private static Func<Requests.RequestLogEntry, bool> With(string marker) => r => r.QueryString?.Contains(marker, StringComparison.Ordinal) == true;

    [Fact]
    public async Task A_request_is_logged_with_its_user_and_redacted_headers_query_and_bodies()
    {
        var marker = Marker();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/test/echo?marker={marker}&api_key=k1")
        {
            Content = JsonContent.Create(new { name = marker, password = "p4ss", nested = new { clientSecret = "s3cret", items = new[] { 1, 2 } }, tokens = new[] { "a", "b" } }),
        };
        request.Headers.Add("X-Api-Key", "cw_secret");
        request.Headers.Add("X-Trace-Me", "visible");

        (await Admin.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var row = await app.LoggedAsync(With(marker));
        row.ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe("POST"),
            r => r.Path.ShouldBe("/api/v1/test/echo"),
            r => r.Route.ShouldBe("/api/v1/test/echo"),
            r => r.RequestType.ShouldBe(nameof(Echo)),
            r => r.StatusCode.ShouldBe(200),
            r => r.UserId.ShouldBe(_setup.AdminUserId),
            r => r.TenantId.ShouldBe(_setup.TenantId),
            r => r.TraceId.ShouldNotBeNullOrEmpty(),
            r => r.QueryString.ShouldBe($"?marker={marker}&api_key=***"));
        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(row.RequestHeaders!)!;
        headers["Authorization"].ShouldBe("***");
        headers["X-Api-Key"].ShouldBe("***");
        headers["X-Trace-Me"].ShouldBe("visible");
        foreach (var body in new[] { row.RequestBody!, row.ResponseBody! })
        {
            using var json = JsonDocument.Parse(body);
            json.RootElement.GetProperty("name").GetString().ShouldBe(marker);
            json.RootElement.GetProperty("password").GetString().ShouldBe("***");
            json.RootElement.GetProperty("nested").GetProperty("clientSecret").GetString().ShouldBe("***");
            json.RootElement.GetProperty("nested").GetProperty("items").GetArrayLength().ShouldBe(2);
            json.RootElement.GetProperty("tokens").GetString().ShouldBe("***");
        }

        row.RequestBody.ShouldNotContain("p4ss");
        row.ResponseBody.ShouldNotContain("s3cret");
    }

    [Fact]
    public async Task Bodies_are_cut_at_the_size_set_and_stay_redacted()
    {
        await app.SetSettingAsync(RequestLogSettings.MaxBodyKb, "1");
        var marker = Marker();

        await Admin.PostAsJsonAsync($"/api/v1/test/echo?marker={marker}", new { marker, password = "p4ss", text = new string('x', 5000), secret = "late" }, Ct);

        var row = await app.LoggedAsync(With(marker));
        Encoding.UTF8.GetByteCount(row.RequestBody!).ShouldBeLessThan(1100);
        row.RequestBody.ShouldEndWith("…");
        row.RequestBody.ShouldContain("\"password\":\"***\"");
        row.RequestBody.ShouldNotContain("p4ss");
        Encoding.UTF8.GetByteCount(row.ResponseBody!).ShouldBeLessThan(1100);
    }

    [Fact]
    public async Task The_setting_logs_reading_writing_or_nothing()
    {
        await app.SetSettingAsync(RequestLogSettings.Requests, RequestLogSettings.Writing);
        var writeOnly = Marker();
        await Admin.GetAsync($"/api/v1/test/echo?q={writeOnly}", Ct);
        await Admin.PostAsJsonAsync($"/api/v1/test/echo?q={writeOnly}", new { }, Ct);
        await app.LoggedAsync(With(writeOnly));

        await app.SetSettingAsync(RequestLogSettings.Requests, RequestLogSettings.Reading);
        var readOnly = Marker();
        await Admin.PostAsJsonAsync($"/api/v1/test/echo?q={readOnly}", new { }, Ct);
        await Admin.GetAsync($"/api/v1/test/echo?q={readOnly}", Ct);
        var read = await app.LoggedAsync(With(readOnly));
        read.ResponseBody.ShouldContain("\"accessToken\":\"***\"");

        await app.SetSettingAsync(RequestLogSettings.Requests, string.Empty);
        var off = Marker();
        await Admin.GetAsync($"/api/v1/test/echo?q={off}", Ct);
        await Admin.PostAsJsonAsync($"/api/v1/test/echo?q={off}", new { }, Ct);

        // the queue keeps its order: once a later request is saved, the earlier ones would have been too
        await app.SetSettingAsync(RequestLogSettings.Requests, null);
        var fence = Marker();
        await Admin.PostAsJsonAsync($"/api/v1/test/echo?q={fence}", new { }, Ct);
        await app.LoggedAsync(With(fence));
        var rows = await app.RequestsAsync();
        rows.Where(With(writeOnly)).Select(r => r.Method).ShouldBe(["POST"]);
        rows.Where(With(readOnly)).Select(r => r.Method).ShouldBe(["GET"]);
        rows.Where(With(off)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Unhandled_exceptions_are_summarised_and_skipped_endpoints_stay_out()
    {
        var marker = Marker();
        await Admin.PostAsync($"/api/v1/test/quiet?q={marker}", null, Ct);
        await Admin.PostAsync($"/api/v1/test/fail?q={marker}", null, Ct);

        var failed = await app.LoggedAsync(r => r.Path == "/api/v1/test/fail" && With(marker)(r));
        failed.StatusCode.ShouldBe(500);
        failed.Exception.ShouldBe("InvalidOperationException: boom");
        (await app.RequestsAsync()).ShouldNotContain(r => r.Path == "/api/v1/test/quiet");
    }

    [Fact]
    public async Task The_list_shows_the_own_tenant_without_headers_and_bodies()
    {
        var (otherUser, otherTenant) = await app.CreateTenantAdminAsync();
        var mine = Marker();
        var theirs = Marker();
        await app.As(otherUser, otherTenant).PostAsJsonAsync($"/api/v1/test/echo?q={theirs}", new { }, Ct);
        await Admin.PostAsJsonAsync($"/api/v1/test/echo?q={mine}", new { }, Ct);
        await app.LoggedAsync(With(mine));

        var list = await Admin.GetStringAsync("/odata/RequestLog?$top=1000", Ct);

        list.ShouldContain(mine);
        list.ShouldNotContain(theirs);
        list.ShouldNotContain("RequestBody");
        list.ShouldNotContain("RequestHeaders");
        (await app.As(otherUser, otherTenant).GetStringAsync("/odata/RequestLog?$top=1000", Ct)).ShouldNotContain(mine);
    }

    [Fact]
    public async Task Reading_the_log_needs_its_permission_and_the_bodies_another()
    {
        var marker = Marker();
        await Admin.PostAsJsonAsync($"/api/v1/test/echo?q={marker}", new { marker }, Ct);
        var row = await app.LoggedAsync(With(marker));
        var nobody = app.As(await app.CreateUserAsync(_setup.TenantId), _setup.TenantId);
        var reader = app.As(await app.CreateUserAsync(_setup.TenantId, AuditPermissions.Requests), _setup.TenantId);
        var bodies = app.As(await app.CreateUserAsync(_setup.TenantId, AuditPermissions.RequestBodies), _setup.TenantId);

        (await nobody.GetAsync("/odata/RequestLog", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.GetAsync($"/api/v1/audit/requests/{row.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.GetAsync("/api/v1/audit/trace-viewer", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync("/odata/RequestLog", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var hidden = (await reader.GetFromJsonAsync<RequestLogDetailDto>($"/api/v1/audit/requests/{row.Id}", Ct))!;
        hidden.BodiesHidden.ShouldBeTrue();
        (hidden.RequestBody, hidden.ResponseBody).ShouldBe((null, null));
        hidden.RequestHeaders["Authorization"].ShouldBe("***");
        hidden.Request.RequestType.ShouldBe(nameof(Echo));

        var shown = (await bodies.GetFromJsonAsync<RequestLogDetailDto>($"/api/v1/audit/requests/{row.Id}", Ct))!;
        shown.BodiesHidden.ShouldBeFalse();
        shown.RequestBody.ShouldContain(marker);
        (await bodies.GetFromJsonAsync<TraceViewerDto>("/api/v1/audit/trace-viewer", Ct))!.UrlTemplate.ShouldBeNull();

        var (otherUser, otherTenant) = await app.CreateTenantAdminAsync();
        (await app.As(otherUser, otherTenant).GetAsync($"/api/v1/audit/requests/{row.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Retention_deletes_old_requests_and_erasure_keeps_them_without_the_user()
    {
        var marker = Marker();
        await Admin.PostAsJsonAsync($"/api/v1/test/echo?q={marker}", new { marker }, Ct);
        var row = await app.LoggedAsync(With(marker));
        await using var scope = app.App.Services.CreateAsyncScope();
        var target = scope.ServiceProvider.GetServices<IRetentionTarget>().Single(t => t.Key == "request-log");
        target.DefaultDays.ShouldBe(30);

        var subject = new PersonalDataSubject(_setup.AdminUserId, _setup.TenantId, "admin@acme.test");
        var privacy = scope.ServiceProvider.GetServices<IPersonalDataContributor>().Single(c => c.Section == "requests");
        JsonSerializer.Serialize(await privacy.ExportAsync(subject, Ct)).ShouldContain("/api/v1/test/echo");
        await privacy.EraseAsync(subject, Ct);
        var erased = (await app.RequestsAsync()).Single(r => r.Id == row.Id);
        (erased.UserId, erased.RequestBody, erased.RequestHeaders, erased.Ip).ShouldBe((PersonalDataSubject.ErasedUserId, null, null, null));
        erased.Path.ShouldBe("/api/v1/test/echo");

        (await target.DeleteAsync(row.At.AddSeconds(1), Ct)).ShouldBeGreaterThan(0);
        (await app.RequestsAsync()).ShouldNotContain(r => r.Id == row.Id);
    }

    /// <summary>The cost on the request path: capture and queue; the save runs in the background. Reports the numbers, fails only on a gross regression.</summary>
    [Fact]
    public async Task Logging_adds_little_time_to_a_request()
    {
        const int Requests = 300;
        var body = new { name = "measure", password = "p", items = Enumerable.Range(0, 50).Select(i => new { i, text = "row " + i }).ToArray() };

        await app.SetSettingAsync(RequestLogSettings.Requests, string.Empty);
        var off = await MeasureAsync();
        await app.SetSettingAsync(RequestLogSettings.Requests, null);
        var on = await MeasureAsync();
        await app.SetSettingAsync(RequestLogSettings.Requests, string.Empty);
        off = Math.Min(off, await MeasureAsync());

        var report = $"Request log overhead: off {off:F3} ms, on {on:F3} ms per request, {on - off:F3} ms more";
        TestContext.Current.SendDiagnosticMessage(report);
        TestContext.Current.TestOutputHelper?.WriteLine(report);
        (on - off).ShouldBeLessThan(2.0, report);

        async Task<double> MeasureAsync()
        {
            for (var i = 0; i < 30; i++)
            {
                await Admin.PostAsJsonAsync("/api/v1/test/echo", body, Ct);
            }

            var watch = Stopwatch.StartNew();
            for (var i = 0; i < Requests; i++)
            {
                (await Admin.PostAsJsonAsync("/api/v1/test/echo", body, Ct)).EnsureSuccessStatusCode();
            }

            return watch.Elapsed.TotalMilliseconds / Requests;
        }
    }
}
