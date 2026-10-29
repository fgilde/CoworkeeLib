using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Coworkee.Contracts.Attachments;
using Coworkee.Contracts.Files;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Diagnostics;
using Coworkee.Infrastructure.Tokens;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files.Tests;

public sealed class FileTokenTests(FilesApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task A_file_token_opens_a_private_file_without_sign_in_inline_or_as_download_until_it_expires()
    {
        var file = await UploadAsync(Admin, "report.pdf", "pdf", "application/pdf");
        var token = await (await Admin.PostAsync($"/api/v1/files/{file.Id}/token", null, Ct)).Content.ReadFromJsonAsync<FileTokenDto>(Ct);
        token!.Url.ShouldBe(FileLinks.Token(token.Token, "report.pdf"));
        var anonymous = app.App.GetTestClient();

        using (var head = await anonymous.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/" + token.Url), Ct))
        {
            (head.StatusCode, head.Content.Headers.ContentType!.MediaType).ShouldBe((HttpStatusCode.OK, "application/pdf"));
        }

        using (var inline = await anonymous.GetAsync("/" + token.Url, Ct))
        {
            inline.Content.Headers.ContentDisposition.ShouldBeNull();
            (await inline.Content.ReadAsStringAsync(Ct)).ShouldBe("pdf");
        }

        using (var download = await anonymous.GetAsync("/" + FileLinks.Token(token.Token, "report.pdf", download: true), Ct))
        {
            download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        }

        await ExpireAsync();
        (await anonymous.GetAsync("/" + token.Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_who_may_open_the_file_gets_a_token_and_never_for_another_organisation()
    {
        var file = await UploadAsync(Admin, "secret.txt", "secret", "text/plain");
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("nofiles@acme.test", "Passw0rd!x", null, null), Ct);
        var user = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!;

        (await app.As(user.Id, _setup.TenantId).PostAsync($"/api/v1/files/{file.Id}/token", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await app.As(_setup.AdminUserId, Guid.CreateVersion7()).PostAsync($"/api/v1/files/{file.Id}/token", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.App.GetTestClient().GetAsync("/" + FileLinks.Token("guessed", "secret.txt"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_attachment_token_opens_the_attachment_without_sign_in_for_readers_of_the_record()
    {
        var ticket = await app.AddTicketAsync(_setup.AdminUserId, _setup.TenantId);
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("doc"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var attachment = await (await Admin.PostAsync($"/{AttachmentUrls.Slot("Tickets", ticket, "docs")}?name=a.txt", content, Ct)).Content.ReadFromJsonAsync<RecordAttachmentDto>(Ct);

        var token = await (await Admin.PostAsync($"/{AttachmentUrls.Content(attachment!)}/token", null, Ct)).Content.ReadFromJsonAsync<FileTokenDto>(Ct);
        using var served = await app.App.GetTestClient().GetAsync("/" + token!.Url, Ct);
        (await served.Content.ReadAsStringAsync(Ct)).ShouldBe("doc");

        (await app.As(_setup.AdminUserId, Guid.CreateVersion7()).PostAsync($"/{AttachmentUrls.Content(attachment!)}/token", null, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await ExpireAsync();
        (await app.App.GetTestClient().GetAsync("/" + token.Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_token_of_a_deleted_file_or_attachment_opens_nothing()
    {
        var file = await UploadAsync(Admin, "gone.txt", "gone", "text/plain");
        var fileToken = await (await Admin.PostAsync($"/api/v1/files/{file.Id}/token", null, Ct)).Content.ReadFromJsonAsync<FileTokenDto>(Ct);
        var ticket = await app.AddTicketAsync(_setup.AdminUserId, _setup.TenantId);
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("doc"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var attachment = await (await Admin.PostAsync($"/{AttachmentUrls.Slot("Tickets", ticket, "docs")}?name=a.txt", content, Ct)).Content.ReadFromJsonAsync<RecordAttachmentDto>(Ct);
        var attachmentToken = await (await Admin.PostAsync($"/{AttachmentUrls.Content(attachment!)}/token", null, Ct)).Content.ReadFromJsonAsync<FileTokenDto>(Ct);

        (await Admin.PostAsJsonAsync("/api/v1/files/delete", new FileSelectionRequest([file.Id], []), Ct)).EnsureSuccessStatusCode();
        (await Admin.DeleteAsync($"/{AttachmentUrls.Content(attachment!)}", Ct)).EnsureSuccessStatusCode();

        var anonymous = app.App.GetTestClient();
        (await anonymous.GetAsync("/" + fileToken!.Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync("/" + attachmentToken!.Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Token_links_are_rate_limited_per_address()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i <= FilesApp.TokenLimit; i++)
        {
            var context = await app.App.GetTestServer().SendAsync(c =>
            {
                c.Request.Method = HttpMethods.Get;
                c.Request.Path = "/" + FileLinks.Token("guessed", "a.txt");
                c.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
            }, Ct);
            statuses.Add((HttpStatusCode)context.Response.StatusCode);
        }

        statuses.Take(FilesApp.TokenLimit).ShouldAllBe(s => s == HttpStatusCode.NotFound);
        statuses[^1].ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Tokens_are_masked_in_request_logs_and_in_the_request_activity()
    {
        var file = await UploadAsync(Admin, "secret.pdf", "pdf", "application/pdf");
        var token = (await (await Admin.PostAsync($"/api/v1/files/{file.Id}/token", null, Ct)).Content.ReadFromJsonAsync<FileTokenDto>(Ct))!;
        var tags = new ConcurrentQueue<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            // as the OpenTelemetry instrumentation does, from the raw request
            ActivityStarted = activity => activity.SetTag("url.full", "http://localhost/" + token.Url),
            ActivityStopped = activity => tags.Enqueue($"{activity.DisplayName} {activity.GetTagItem("url.full")} {activity.GetTagItem("url.path")}"),
        };
        ActivitySource.AddActivityListener(listener);

        (await app.App.GetTestClient().GetAsync("/" + token.Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        app.Logs.Lines.ShouldContain(line => line.Contains("/api/v1/files/token/***/secret.pdf"));
        app.Logs.Lines.ShouldContain(line => line.Contains("RequestPath=/api/v1/files/token/***/secret.pdf"), "the hosting scope of the request");
        app.Logs.Lines.ShouldNotContain(line => line.Contains(token.Token));
        tags.ShouldContain(t => t.Contains("/api/v1/files/token/***/secret.pdf"));
        tags.ShouldNotContain(t => t.Contains(token.Token));
    }

    [Fact]
    public void Tokens_are_masked_in_outgoing_and_forwarded_request_activities()
    {
        var stopped = new ConcurrentQueue<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "System.Net.Http" or "Yarp.ReverseProxy",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stopped.Enqueue($"{activity.DisplayName} {activity.GetTagItem("url.full")}"),
        };
        ActivitySource.AddActivityListener(listener);

        foreach (var name in new[] { "System.Net.Http", "Yarp.ReverseProxy" })
        {
            using var source = new ActivitySource(name);
            using var activity = source.StartActivity("GET /api/v1/data/downloads/s3cr3t")!;
            activity.SetTag("url.full", "http://api/api/v1/files/token/s3cr3t/a.pdf");
        }

        stopped.Count.ShouldBe(2);
        stopped.ShouldAllBe(s => !s.Contains("s3cr3t") && s.Contains("/token/***/") && s.Contains("/downloads/***"));
    }

    [Fact]
    public async Task Tokens_in_query_strings_are_masked_in_request_logs_and_activities()
    {
        var tags = new ConcurrentQueue<string>();
        const string query = "?access_token=s3cr3t-at&id_token_hint=s3cr3t-hint&page=2&code=s3cr3t-code&refresh_token=s3cr3t-rt";
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => activity.SetTag("url.query", query[1..]).SetTag("url.full", "http://localhost/hubs/realtime" + query),
            ActivityStopped = activity => tags.Enqueue($"{activity.GetTagItem("url.full")} {activity.GetTagItem("url.query")}"),
        };
        ActivitySource.AddActivityListener(listener);

        await app.App.GetTestClient().GetAsync("/hubs/realtime" + query, Ct);

        // Microsoft.AspNetCore.Hosting.Diagnostics: "Request starting ... /hubs/realtime?access_token=..."
        app.Logs.Lines.ShouldContain(line => line.Contains("/hubs/realtime?access_token=***&id_token_hint=***&page=2&code=***&refresh_token=***"));
        app.Logs.Lines.ShouldNotContain(line => line.Contains("s3cr3t"));
        tags.ShouldContain(t => t.Contains("access_token=***&id_token_hint=***&page=2"));
        tags.ShouldNotContain(t => t.Contains("s3cr3t"));
        SecretPaths.Redact("https://auth/connect/logout?id_token_hint=eyJ.x.y&post_logout_redirect_uri=https://app").ShouldBe("https://auth/connect/logout?id_token_hint=***&post_logout_redirect_uri=https://app");
        SecretPaths.Redact("/odata/Codes?$filter=code eq 'x'&encode=1").ShouldBe("/odata/Codes?$filter=code eq 'x'&encode=1");
    }

    // the clock moves on: the tokens' time is up
    private Task ExpireAsync() => app.InDbAsync(db => db.Set<AccessToken>().ExecuteUpdateAsync(t => t.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)), Ct));

    private static async Task<StoredFileDto> UploadAsync(HttpClient client, string name, string text, string contentType)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var response = await client.PostAsync($"/api/v1/files?name={Uri.EscapeDataString(name)}", content, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StoredFileDto>(Ct))!;
    }
}
