using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Coworkee.Contracts.Attachments;
using Coworkee.Contracts.Files;
using Coworkee.Contracts.Identity;
using Coworkee.Infrastructure.Tokens;
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
