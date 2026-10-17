using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Auditing;
using Coworkee.Contracts.Identity;

namespace Coworkee.Auditing.Tests;

public sealed class EntityHistoryTests(AuditApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private Task<Guid> CreateAsync(string text) => app.CreateMemoAsync(_setup.AdminUserId, _setup.TenantId, text);

    private Task ChangeAsync(Guid id, Action<Memo> change) => app.ChangeMemoAsync(_setup.AdminUserId, _setup.TenantId, id, change);

    private async Task<EntityHistoryPreviewDto> PreviewAsync(Guid id, EntityHistoryRestoreRequest request) =>
        (await (await Admin.PostAsJsonAsync($"/api/v1/history/Memos/{id}/preview", request, Ct)).Content.ReadFromJsonAsync<EntityHistoryPreviewDto>(Ct))!;

    private async Task<HttpResponseMessage> RestoreAsync(Guid id, EntityHistoryRestoreRequest request) =>
        await Admin.PostAsJsonAsync($"/api/v1/history/Memos/{id}/restore", request with { Version = (await PreviewAsync(id, request)).Version }, Ct);

    private async Task<EntityHistoryDto> HistoryAsync(Guid id) => (await Admin.GetFromJsonAsync<EntityHistoryDto>($"/api/v1/history/Memos/{id}", Ct))!;

    [Fact]
    public async Task History_lists_changes_newest_first_with_readable_values()
    {
        var id = await CreateAsync("one");
        await ChangeAsync(id, m => m.Priority = MemoPriority.High);

        var history = await HistoryAsync(id);

        history.CanRestore.ShouldBeTrue();
        history.Entries.Select(e => e.Action).ShouldBe(["Updated", "Created"]);
        history.Entries[0].ActorName.ShouldBe("Ada Admin");
        var priority = history.Entries[0].Changes.Single();
        (priority.Property, priority.OldText, priority.NewText, priority.Restorable).ShouldBe(("Priority", "Low", "High", true));
        var secret = history.Entries[1].Changes.Single(c => c.Property == "Secret");
        (secret.NewText, secret.Restorable).ShouldBe(("***", false));
    }

    [Fact]
    public async Task Restore_to_a_point_in_time_goes_through_the_update_command()
    {
        var id = await CreateAsync("one");
        await ChangeAsync(id, m => m.Text = "two");
        await ChangeAsync(id, m => (m.Text, m.Priority) = ("three", MemoPriority.High));
        var created = (await HistoryAsync(id)).Entries[^1].Id;

        var preview = await (await Admin.PostAsJsonAsync($"/api/v1/history/Memos/{id}/preview", new EntityHistoryRestoreRequest(created), Ct))
            .Content.ReadFromJsonAsync<EntityHistoryPreviewDto>(Ct);
        preview!.Undeletes.ShouldBeFalse();
        preview.Fields.Select(f => (f.Property, f.CurrentText, f.RestoredText)).ShouldBe([("Text", "three", "one"), ("Priority", "High", "Low")], ignoreOrder: true);

        (await RestoreAsync(id, new EntityHistoryRestoreRequest(created))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var memo = await app.MemoAsync(id);
        (memo.Text, memo.Priority, memo.Secret).ShouldBe(("one", MemoPriority.Low, "s1"));
        (await HistoryAsync(id)).Entries[0].ActorId.ShouldBe(_setup.AdminUserId);
    }

    [Fact]
    public async Task A_single_change_can_be_undone()
    {
        var id = await CreateAsync("one");
        await ChangeAsync(id, m => m.Text = "two");
        await ChangeAsync(id, m => m.Priority = MemoPriority.High);
        var textChange = (await HistoryAsync(id)).Entries[1].Id;

        (await RestoreAsync(id, new EntityHistoryRestoreRequest(textChange, "Text"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var memo = await app.MemoAsync(id);
        (memo.Text, memo.Priority).ShouldBe(("one", MemoPriority.High));
    }

    [Fact]
    public async Task Undo_of_an_entry_shows_the_fields_a_later_change_wrote_and_then_replaces_them()
    {
        var id = await CreateAsync("one");
        await ChangeAsync(id, m => (m.Text, m.Priority) = ("two", MemoPriority.High));
        await ChangeAsync(id, m => m.Text = "three");
        var entry = (await HistoryAsync(id)).Entries[1].Id;
        var undo = new EntityHistoryRestoreRequest(entry, Mode: EntityHistoryRestoreMode.Undo);

        var preview = await PreviewAsync(id, undo);

        preview.Fields.Select(f => (f.Property, f.CurrentText, f.RestoredText, f.ChangedLater))
            .ShouldBe([("Text", "three", "one", true), ("Priority", "High", "Low", false)], ignoreOrder: true);
        (await RestoreAsync(id, undo)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var memo = await app.MemoAsync(id);
        (memo.Text, memo.Priority).ShouldBe(("one", MemoPriority.Low));
    }

    [Fact]
    public async Task Undo_keeps_later_changes_of_other_fields_and_before_rolls_them_back()
    {
        var id = await CreateAsync("one");
        await ChangeAsync(id, m => m.Text = "two");
        await ChangeAsync(id, m => m.Priority = MemoPriority.High);
        var entry = (await HistoryAsync(id)).Entries[1].Id;

        var before = await PreviewAsync(id, new EntityHistoryRestoreRequest(entry, Mode: EntityHistoryRestoreMode.Before));
        before.Fields.Select(f => (f.Property, f.RestoredText, f.ChangedLater)).ShouldBe([("Text", "one", false), ("Priority", "Low", false)], ignoreOrder: true);

        var undo = new EntityHistoryRestoreRequest(entry, Mode: EntityHistoryRestoreMode.Undo);
        (await PreviewAsync(id, undo)).Fields.Single().ShouldBe(new EntityHistoryFieldDto("Text", "two", "one"));
        (await RestoreAsync(id, undo)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var memo = await app.MemoAsync(id);
        (memo.Text, memo.Priority).ShouldBe(("one", MemoPriority.High));
    }

    [Fact]
    public async Task A_restore_of_a_record_changed_since_its_preview_is_refused()
    {
        var id = await CreateAsync("one");
        await ChangeAsync(id, m => m.Text = "two");
        var request = new EntityHistoryRestoreRequest((await HistoryAsync(id)).Entries[^1].Id);
        var preview = await PreviewAsync(id, request);
        await ChangeAsync(id, m => m.Priority = MemoPriority.High);

        var response = await Admin.PostAsJsonAsync($"/api/v1/history/Memos/{id}/restore", request with { Version = preview.Version }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("changed since the preview");
        (await Admin.PostAsJsonAsync($"/api/v1/history/Memos/{id}/restore", request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await app.MemoAsync(id)).Text.ShouldBe("two");
        (await PreviewAsync(id, request)).Version.ShouldNotBe(preview.Version);
    }

    [Fact]
    public async Task The_sets_with_a_history_say_whether_the_user_may_restore()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("eve@acme.test", "Passw0rd!x", null, null), Ct);
        var eve = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!;

        (await Admin.GetFromJsonAsync<EntityHistorySetDto[]>("/api/v1/history", Ct)).ShouldBe([new EntityHistorySetDto("Memo", "Memos", true, true)]);
        (await app.As(eve.Id, _setup.TenantId).GetFromJsonAsync<EntityHistorySetDto[]>("/api/v1/history", Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_command_validation_applies_and_nothing_changes()
    {
        var id = await CreateAsync("locked");
        await ChangeAsync(id, m => m.Text = "open");
        var created = (await HistoryAsync(id)).Entries[^1].Id;

        (await RestoreAsync(id, new EntityHistoryRestoreRequest(created))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await app.MemoAsync(id)).Text.ShouldBe("open");
    }

    [Fact]
    public async Task Soft_deleted_records_are_listed_and_restored()
    {
        var id = await CreateAsync("one");
        await ChangeAsync(id, m => m.Text = "two");
        await app.DeleteMemoAsync(_setup.AdminUserId, _setup.TenantId, id);

        var deleted = await Admin.GetFromJsonAsync<DeletedRecordDto[]>("/api/v1/history/Memos/deleted", Ct);
        deleted!.Single().ShouldSatisfyAllConditions(d => d.EntityId.ShouldBe(id.ToString()), d => d.Title.ShouldBe("two"), d => d.DeletedByName.ShouldBe("Ada Admin"));
        var history = await HistoryAsync(id);
        history.IsDeleted.ShouldBeTrue();
        history.Entries[0].Action.ShouldBe("Deleted");

        var preview = await (await Admin.PostAsJsonAsync($"/api/v1/history/Memos/{id}/preview", new EntityHistoryRestoreRequest(history.Entries[0].Id), Ct))
            .Content.ReadFromJsonAsync<EntityHistoryPreviewDto>(Ct);
        preview!.Undeletes.ShouldBeTrue();
        (await RestoreAsync(id, new EntityHistoryRestoreRequest(history.Entries[^1].Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var memo = await app.MemoAsync(id);
        (memo.IsDeleted, memo.Text).ShouldBe((false, "one"));
        (await HistoryAsync(id)).Entries.Select(e => e.Action).Take(2).ShouldBe(["Updated", "Restored"]);
    }

    [Fact]
    public async Task History_needs_the_audit_permission_and_stays_in_the_tenant()
    {
        var id = await CreateAsync("one");
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct);
        var bob = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var (otherUser, otherTenant) = await app.CreateTenantAdminAsync();

        (await app.As(bob.Id, _setup.TenantId).GetAsync($"/api/v1/history/Memos/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await app.As(otherUser, otherTenant).GetAsync($"/api/v1/history/Memos/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.GetAsync($"/api/v1/history/Nope/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
