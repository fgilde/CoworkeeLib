using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coworkee.Contracts.Files;
using Coworkee.Contracts.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Files.Tests;

public sealed class FileTests(FilesApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Uploads_and_serves_a_file_inline_in_a_sandbox_and_as_download()
    {
        var file = await UploadAsync(Admin, null, "page.html", "<script>alert(1)</script>", "text/html");

        file.Size.ShouldBe(25);
        file.ContentType.ShouldBe("text/html");
        file.CreatedBy.ShouldBe(_setup.AdminUserId);
        using var inline = await Admin.GetAsync($"/api/v1/files/{file.Id}/content", Ct);
        inline.Headers.GetValues("Content-Security-Policy").Single().ShouldStartWith("sandbox;");
        inline.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        inline.Content.Headers.ContentDisposition.ShouldBeNull();
        (await inline.Content.ReadAsStringAsync(Ct)).ShouldBe("<script>alert(1)</script>");
        using var download = await Admin.GetAsync($"/api/v1/files/{file.Id}/content?download=true", Ct);
        download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        download.Content.Headers.ContentDisposition.FileNameStar.ShouldBe("page.html");
    }

    [Fact]
    public async Task Folders_nest_files_move_and_deleting_a_folder_soft_deletes_its_subtree()
    {
        var docs = await CreateFolderAsync(null, "Docs");
        var inner = await CreateFolderAsync(docs.Id, "Inner");
        var file = await UploadAsync(Admin, null, "a.txt", "a");
        (await Admin.PostAsJsonAsync("/api/v1/files/move", new MoveRequest([file.Id], [], inner.Id), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/files/{file.Id}/name", new RenameRequest("b.txt"), Ct)).EnsureSuccessStatusCode();

        var content = await ContentAsync(Admin, inner.Id);
        content.Files.ShouldHaveSingleItem().Name.ShouldBe("b.txt");
        (await Admin.PostAsJsonAsync("/api/v1/files/move", new MoveRequest([], [docs.Id], inner.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Admin.PostAsJsonAsync("/api/v1/files/delete", new FileSelectionRequest([], [docs.Id]), Ct)).EnsureSuccessStatusCode();

        (await ContentAsync(Admin, null)).Folders.ShouldBeEmpty();
        (await Admin.GetAsync($"/api/v1/files/{file.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.InDbAsync(db => db.Set<StoredFile>().IgnoreQueryFilters().SingleAsync(f => f.Id == file.Id, Ct))).IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_grant_on_a_folder_covers_its_subfolders_only()
    {
        var team = await CreateFolderAsync(null, "Team");
        var sub = await CreateFolderAsync(team.Id, "Sub");
        var other = await CreateFolderAsync(null, "Other");
        var user = await PostAsync<UserDto>("/api/v1/identity/users", new CreateUserRequest("tom@acme.test", "Passw0rd!x", null, null));
        var role = await PostAsync<Guid>("/api/v1/identity/roles", new RoleRequest("Team uploader", null));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([FilePermissions.Upload]), Ct)).EnsureSuccessStatusCode();
        await PostAsync<Guid>($"/api/v1/identity/resource-permissions/{FilePermissions.FolderResource}/{team.Id}", new GrantResourcePermissionRequest(PrincipalType.User, user.Id, role));
        var tom = app.As(user.Id, _setup.TenantId);

        (await ContentAsync(tom, null)).Folders.ShouldHaveSingleItem().Id.ShouldBe(team.Id);
        (await ContentAsync(tom, sub.Id)).CanUpload.ShouldBeTrue();
        await UploadAsync(tom, sub.Id, "mine.txt", "x");
        (await tom.GetAsync($"/api/v1/files/folders/content?folderId={other.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Upload(tom, other.Id, "no.txt", "x")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Upload(tom, null, "no.txt", "x")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Files_are_searchable_through_odata()
    {
        await UploadAsync(Admin, null, "report-2026.pdf", "%PDF", "application/pdf");
        await UploadAsync(Admin, null, "notes.txt", "n");

        var page = await Admin.GetFromJsonAsync<JsonElement>("/odata/StoredFiles?$filter=contains(Name,'report')", Ct);

        var row = page.GetProperty("value").EnumerateArray().ShouldHaveSingleItem();
        row.GetProperty("Name").GetString().ShouldBe("report-2026.pdf");
        row.TryGetProperty("BlobKey", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Registration_documents_land_in_a_folder_only_the_user_and_admins_see()
    {
        var nia = await PostAsync<UserDto>("/api/v1/identity/users", new CreateUserRequest("nia@acme.test", "Passw0rd!x", null, null));
        var tom = await PostAsync<UserDto>("/api/v1/identity/users", new CreateUserRequest("tom@acme.test", "Passw0rd!x", null, null));
        using (Core.Security.CurrentUserScope.Begin(new Core.Security.ImpersonatedUser(nia.Id, _setup.TenantId)))
        {
            await using var scope = app.App.Services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<Application.Registration.IRegistrationDocumentStore>();
            var slot = new Contracts.Configuration.RegistrationDocumentSlot { Name = "Passport" };
            foreach (var name in new[] { "front.png", "back.png" })
            {
                await store.SaveAsync(new Application.Registration.RegistrationDocument(nia.Id, nia.Email, slot, name, "image/png", 3, new MemoryStream([1, 2, 3])), Ct);
            }

            await scope.ServiceProvider.GetRequiredService<FilesTestDbContext>().SaveChangesAsync(Ct);
        }

        var mine = (await ContentAsync(app.As(nia.Id, _setup.TenantId), null)).Folders.ShouldHaveSingleItem();
        mine.Name.ShouldBe("nia@acme.test");
        (await ContentAsync(app.As(nia.Id, _setup.TenantId), mine.Id)).Files.Select(f => f.Name).ShouldBe(["Passport - back.png", "Passport - front.png"]);
        (await ContentAsync(app.As(nia.Id, _setup.TenantId), mine.Id)).CanUpload.ShouldBeFalse();
        (await ContentAsync(app.As(tom.Id, _setup.TenantId), null)).Folders.ShouldBeEmpty();
        var registrations = (await ContentAsync(Admin, null)).Folders.ShouldHaveSingleItem();
        registrations.Name.ShouldBe("Registrations");
        (await ContentAsync(Admin, registrations.Id)).Folders.ShouldHaveSingleItem().Id.ShouldBe(mine.Id);
    }

    private async Task<FolderDto> CreateFolderAsync(Guid? parentId, string name) => await PostAsync<FolderDto>("/api/v1/files/folders", new CreateFolderRequest(parentId, name));

    private static async Task<FolderContentDto> ContentAsync(HttpClient client, Guid? folderId) =>
        (await client.GetFromJsonAsync<FolderContentDto>("/api/v1/files/folders/content" + (folderId is null ? string.Empty : $"?folderId={folderId}"), Ct))!;

    private static async Task<StoredFileDto> UploadAsync(HttpClient client, Guid? folderId, string name, string text, string contentType = "text/plain")
    {
        var response = await Upload(client, folderId, name, text, contentType);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StoredFileDto>(Ct))!;
    }

    private static Task<HttpResponseMessage> Upload(HttpClient client, Guid? folderId, string name, string text, string contentType = "text/plain")
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return client.PostAsync($"/api/v1/files?name={Uri.EscapeDataString(name)}" + (folderId is null ? string.Empty : $"&folderId={folderId}"), content, Ct);
    }

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Admin.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }
}
