using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Backup;
using Coworkee.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Backup.Tests;

public sealed class BackupTests(BackupApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(db =>
        {
            db.AddRange(new Note { Text = "first" }, new Note { Text = "second" });
            return db.SaveChangesAsync();
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task A_restore_brings_back_every_table_as_it_was()
    {
        var backup = await (await Admin.PostAsync("/api/v1/backups", null, Ct)).Content.ReadFromJsonAsync<BackupDto>(Ct);
        backup!.Tables.ShouldBeGreaterThan(1);
        await app.InDbAsync(async db =>
        {
            db.RemoveRange(await db.Set<Note>().Where(n => n.Text == "first").ToListAsync(Ct));
            (await db.Set<Note>().SingleAsync(n => n.Text == "second", Ct)).Text = "changed";
            db.Add(new Note { Text = "later" });
            return await db.SaveChangesAsync(Ct);
        });

        (await Admin.PostAsync($"/api/v1/backups/{backup.Id}/restore", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var texts = await app.InDbAsync(db => db.Set<Note>().OrderBy(n => n.Text).Select(n => n.Text).ToListAsync(Ct));
        texts.ShouldBe(["first", "second"]);
        (await Admin.GetFromJsonAsync<List<BackupDto>>("/api/v1/backups", Ct))!.Single().Id.ShouldBe(backup.Id);
    }

    [Fact]
    public async Task Backups_download_as_zip_and_can_be_deleted()
    {
        var backup = await (await Admin.PostAsync("/api/v1/backups", null, Ct)).Content.ReadFromJsonAsync<BackupDto>(Ct);

        var zip = await Admin.GetByteArrayAsync($"/api/v1/backups/{backup!.Id}/download", Ct);
        zip.Take(2).ShouldBe("PK"u8.ToArray());
        zip.Length.ShouldBe((int)backup.Size);

        (await Admin.DeleteAsync($"/api/v1/backups/{backup.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Admin.GetFromJsonAsync<List<BackupDto>>("/api/v1/backups", Ct))!.ShouldBeEmpty();
        (await Admin.GetAsync($"/api/v1/backups/{backup.Id}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_downloaded_backup_uploads_again_and_restores()
    {
        var made = await (await Admin.PostAsync("/api/v1/backups", null, Ct)).Content.ReadFromJsonAsync<BackupDto>(Ct);
        var zip = await Admin.GetByteArrayAsync($"/api/v1/backups/{made!.Id}/download", Ct);
        await Admin.DeleteAsync($"/api/v1/backups/{made.Id}", Ct);
        await app.InDbAsync(async db =>
        {
            db.Add(new Note { Text = "later" });
            return await db.SaveChangesAsync(Ct);
        });

        var response = await Admin.PostAsync("/api/v1/backups/upload?name=from-elsewhere", new ByteArrayContent(zip), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var uploaded = await response.Content.ReadFromJsonAsync<BackupDto>(Ct);
        uploaded!.Name.ShouldBe("from-elsewhere.zip");
        uploaded.Tables.ShouldBe(made.Tables);
        uploaded.Migration.ShouldBe(made.Migration);

        (await Admin.PostAsync($"/api/v1/backups/{uploaded.Id}/restore", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await app.InDbAsync(db => db.Set<Note>().Select(n => n.Text).ToListAsync(Ct))).ShouldNotContain("later");
    }

    [Fact]
    public async Task An_upload_that_is_no_backup_is_refused()
    {
        (await Admin.PostAsync("/api/v1/backups/upload?name=notes.zip", new ByteArrayContent("hello"u8.ToArray()), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.GetFromJsonAsync<List<BackupDto>>("/api/v1/backups", Ct))!.ShouldBeEmpty();
    }
}
