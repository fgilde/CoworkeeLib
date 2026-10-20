using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Coworkee.Application.Retention;
using Coworkee.Contracts.Attachments;
using Coworkee.Contracts.Files;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Security;
using Coworkee.Files.Attachments;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Files.Tests;

public sealed class AttachmentTests(FilesApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _ticket;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _ticket = await app.AddTicketAsync(_setup.AdminUserId, _setup.TenantId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private static string Slot(Guid ticket, string slot) => "/" + AttachmentUrls.Slot("Tickets", ticket, slot);

    [Fact]
    public async Task Uploads_list_serve_inline_or_as_download_replace_in_a_single_slot_and_delete()
    {
        var first = await UploadAsync(Admin, _ticket, "proof", "scan.pdf", "%PDF-1", "application/pdf");
        var second = await UploadAsync(Admin, _ticket, "proof", "photo.png", "png", "image/png");
        await UploadAsync(Admin, _ticket, "docs", "a.txt", "a");
        var docB = await UploadAsync(Admin, _ticket, "docs", "b.txt", "b");

        var slots = (await Admin.GetFromJsonAsync<List<AttachmentSlotDto>>($"/api/v1/attachments/Tickets/{_ticket}", Ct))!;
        slots.Select(s => (s.Slot, s.Multiple, s.CanWrite, s.Items.Count)).ShouldBe([("proof", false, true, 1), ("docs", true, true, 2)]);
        slots[0].Items.ShouldHaveSingleItem().Id.ShouldBe(second.Id);
        slots[0].ContentTypes.ShouldBe(["application/pdf", "image/*"]);
        (await Admin.GetAsync("/" + AttachmentUrls.Content(first), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var inline = await Admin.GetAsync("/" + AttachmentUrls.Content(second), Ct);
        inline.Headers.GetValues("Content-Security-Policy").Single().ShouldStartWith("sandbox;");
        (await inline.Content.ReadAsStringAsync(Ct)).ShouldBe("png");
        using var download = await Admin.GetAsync("/" + AttachmentUrls.Content(second, download: true), Ct);
        download.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("photo.png");
        (await Admin.GetAsync($"{Slot(_ticket, "docs")}/{second.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Admin.DeleteAsync("/" + AttachmentUrls.Content(docB), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Admin.GetFromJsonAsync<AttachmentSlotDto>(Slot(_ticket, "docs"), Ct))!.Items.ShouldHaveSingleItem().FileName.ShouldBe("a.txt");
    }

    [Fact]
    public async Task Size_type_name_scanner_and_unknown_slots_are_refused()
    {
        (await Upload(Admin, _ticket, "proof", "big.pdf", new string('x', 1025), "application/pdf")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Upload(Admin, _ticket, "proof", "notes.txt", "text")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Upload(Admin, _ticket, "docs", "virus.exe", "MZ")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Upload(Admin, _ticket, "docs", "../up.txt", "x")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Upload(Admin, _ticket, "other", "a.txt", "x")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.GetAsync($"/api/v1/attachments/Unknown/{_ticket}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.GetAsync($"/api/v1/attachments/Tickets/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reading_follows_the_row_filter_of_the_set_and_writing_needs_the_write_permission()
    {
        var hidden = await app.AddTicketAsync(_setup.AdminUserId, _setup.TenantId, Ticket.Private);
        var file = await UploadAsync(Admin, hidden, "docs", "secret.txt", "s");
        var shared = await UploadAsync(Admin, _ticket, "docs", "shared.txt", "x");
        var uploader = app.As((await CreateUserAsync("up@acme.test", FilePermissions.Upload)).Id, _setup.TenantId);
        var reader = app.As((await CreateUserAsync("read@acme.test")).Id, _setup.TenantId);

        (await uploader.GetAsync($"/api/v1/attachments/Tickets/{hidden}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await uploader.GetAsync(Slot(hidden, "docs"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await uploader.GetAsync("/" + AttachmentUrls.Content(file), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Upload(uploader, hidden, "docs", "mine.txt", "m")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await uploader.DeleteAsync("/" + AttachmentUrls.Content(file), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Upload(uploader, _ticket, "docs", "mine.txt", "m")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await reader.GetFromJsonAsync<AttachmentSlotDto>(Slot(_ticket, "docs"), Ct))!.CanWrite.ShouldBeFalse();
        (await reader.GetAsync("/" + AttachmentUrls.Content(shared), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Upload(reader, _ticket, "docs", "no.txt", "n")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.DeleteAsync("/" + AttachmentUrls.Content(shared), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Changes_reach_followers_of_the_record_topic_who_see_the_record()
    {
        var hidden = await app.AddTicketAsync(_setup.AdminUserId, _setup.TenantId, Ticket.Private);
        var reader = await CreateUserAsync("read@acme.test");
        var (connection, events) = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await using var _ = connection;
        var (other, _) = await app.ConnectAsync(reader.Id, _setup.TenantId);
        await using var __ = other;
        await connection.InvokeAsync(RealtimeHubMethods.Subscribe, AttachmentTopics.Record("Tickets", _ticket), Ct);
        await Should.ThrowAsync<HubException>(() => other.InvokeAsync(RealtimeHubMethods.Subscribe, AttachmentTopics.Record("Tickets", hidden), Ct));
        await Should.ThrowAsync<HubException>(() => connection.InvokeAsync(RealtimeHubMethods.Subscribe, AttachmentTopics.Record("Unknown", _ticket), Ct));

        var file = await UploadAsync(Admin, _ticket, "docs", "live.txt", "x");
        (await Admin.DeleteAsync("/" + AttachmentUrls.Content(file), Ct)).EnsureSuccessStatusCode();

        for (var i = 0; i < 50 && events.Count < 2; i++)
        {
            await Task.Delay(100, Ct);
        }

        events.Where(e => e.Topic == AttachmentTopics.Record("Tickets", _ticket)).Count().ShouldBe(2);
    }

    [Fact]
    public async Task The_personal_data_export_lists_the_uploads_and_erasing_the_user_keeps_the_files()
    {
        var uploader = await CreateUserAsync("up@acme.test", FilePermissions.Upload);
        var asUploader = app.As(uploader.Id, _setup.TenantId);
        var file = await UploadAsync(asUploader, _ticket, "docs", "cv.txt", "me");
        await UploadAsync(Admin, _ticket, "docs", "other.txt", "x");

        var export = await asUploader.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/identity/me/personal-data", Ct);
        var item = export.GetProperty("attachments").EnumerateArray().ShouldHaveSingleItem();
        (item.GetProperty("fileName").GetString(), item.GetProperty("entityId").GetGuid(), item.GetProperty("slot").GetString()).ShouldBe(("cv.txt", _ticket, "docs"));

        (await Admin.DeleteAsync($"/api/v1/identity/users/{uploader.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Admin.GetFromJsonAsync<AttachmentSlotDto>(Slot(_ticket, "docs"), Ct))!.Items.Select(a => a.Id).ShouldContain(file.Id);
        (await Admin.GetAsync("/" + AttachmentUrls.Content(file), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Retention_removes_the_files_of_records_deleted_for_good_and_keeps_those_of_soft_deleted_ones()
    {
        var gone = await app.AddTicketAsync(_setup.AdminUserId, _setup.TenantId, "gone");
        var trashed = await app.AddTicketAsync(_setup.AdminUserId, _setup.TenantId, "trashed");
        await UploadAsync(Admin, gone, "docs", "a.txt", "a");
        var kept = await UploadAsync(Admin, trashed, "docs", "b.txt", "b");
        await app.InDbAsync(async db =>
        {
            await db.Set<Ticket>().IgnoreQueryFilters().Where(t => t.Id == gone).ExecuteDeleteAsync(Ct);
            return await db.Set<Ticket>().IgnoreQueryFilters().Where(t => t.Id == trashed)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true).SetProperty(t => t.DeletedAt, DateTimeOffset.UtcNow), Ct);
        });

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = app.App.Services.CreateAsyncScope();
        var target = scope.ServiceProvider.GetServices<IRetentionTarget>().Single(t => t.Key == "orphaned-attachments");
        var before = DateTimeOffset.UtcNow.AddMinutes(1);

        (await target.CountAsync(before, Ct)).ShouldBe(1);
        (await target.DeleteAsync(before, Ct)).ShouldBe(1);
        (await app.InDbAsync(db => db.Set<RecordAttachment>().IgnoreQueryFilters().Select(a => a.Id).ToListAsync(Ct))).ShouldBe([kept.Id]);
    }

    private async Task<UserDto> CreateUserAsync(string email, params string[] permissions)
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        if (permissions.Length > 0)
        {
            var role = await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest($"Role {email}", null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
            (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
            (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        }

        return user;
    }

    private static async Task<RecordAttachmentDto> UploadAsync(HttpClient client, Guid ticket, string slot, string name, string text, string contentType = "text/plain")
    {
        var response = await Upload(client, ticket, slot, name, text, contentType);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecordAttachmentDto>(Ct))!;
    }

    private static Task<HttpResponseMessage> Upload(HttpClient client, Guid ticket, string slot, string name, string text, string contentType = "text/plain")
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return client.PostAsync($"{Slot(ticket, slot)}?name={Uri.EscapeDataString(name)}", content, Ct);
    }
}
