using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Auditing;
using Coworkee.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Auditing.Tests;

public sealed class AuditTests(AuditApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Audit_lists_changes_of_the_current_tenant_only()
    {
        var mine = await app.CreateNoteAsync(_setup.AdminUserId, _setup.TenantId, "mine");
        var (otherUser, otherTenant) = await app.CreateTenantAdminAsync();
        var theirs = await app.CreateNoteAsync(otherUser, otherTenant, "theirs");

        var page = await Admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>("/api/v1/audit?entityType=Note", Ct);

        page!.Items.Select(e => e.EntityId).ShouldBe([mine.ToString()]);
        var entry = page.Items.Single();
        entry.Action.ShouldBe("Created");
        entry.ActorName.ShouldBe("Ada Admin");
        entry.Changes.ShouldContain(c => c.Property == "Text" && c.NewValue == "\"mine\"");
        (await app.As(otherUser, otherTenant).GetFromJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/audit?entityId={mine}", Ct))!.Items.ShouldBeEmpty();
        _ = theirs;
    }

    [Fact]
    public async Task Audit_filters_by_entity_actor_and_time()
    {
        var first = await app.CreateNoteAsync(_setup.AdminUserId, _setup.TenantId, "one");
        await app.CreateNoteAsync(_setup.AdminUserId, _setup.TenantId, "two");
        await app.UpdateNoteAsync(_setup.AdminUserId, _setup.TenantId, first, "one more");

        var byEntity = await Admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/audit?entityType=Note&entityId={first}", Ct);
        byEntity!.Items.Select(e => e.Action).ShouldBe(["Updated", "Created"]);

        var byActor = await Admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/audit?entityType=Note&actorId={Guid.CreateVersion7()}", Ct);
        byActor!.Items.ShouldBeEmpty();

        var future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("o"));
        (await Admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/audit?entityType=Note&from={future}", Ct))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Audit_requires_permission()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct);
        var bob = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!;

        (await app.As(bob.Id, _setup.TenantId).GetAsync("/api/v1/audit", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await app.As(bob.Id, _setup.TenantId).GetAsync($"/api/v1/versions/Note/{Guid.CreateVersion7()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Versions_list_revisions_newest_first()
    {
        var id = await app.CreateNoteAsync(_setup.AdminUserId, _setup.TenantId, "v1");
        await app.UpdateNoteAsync(_setup.AdminUserId, _setup.TenantId, id, "v2");

        var versions = await Admin.GetFromJsonAsync<EntityVersionDto[]>($"/api/v1/versions/Note/{id}", Ct);

        versions!.Select(v => v.Revision).ShouldBe([2, 1]);
        versions[0].CreatedByName.ShouldBe("Ada Admin");
        var detail = await Admin.GetFromJsonAsync<EntityVersionDetailDto>($"/api/v1/versions/Note/{id}/1", Ct);
        detail!.Payload.GetProperty("Text").GetString().ShouldBe("v1");
    }

    [Fact]
    public async Task Restore_applies_the_payload_as_a_new_revision_and_audits_it()
    {
        var id = await app.CreateNoteAsync(_setup.AdminUserId, _setup.TenantId, "v1");
        await app.UpdateNoteAsync(_setup.AdminUserId, _setup.TenantId, id, "v2");

        (await Admin.PostAsync($"/api/v1/versions/Note/{id}/1/restore", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var note = await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, db => db.Set<Note>().SingleAsync(n => n.Id == id, Ct));
        note.Text.ShouldBe("v1");
        note.Revision.ShouldBe(3);
        var audit = await Admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/audit?entityType=Note&entityId={id}", Ct);
        audit!.Items.ShouldContain(e => e.Action == "Restored");
    }

    [Fact]
    public async Task Other_tenant_cannot_see_or_restore_my_versions()
    {
        var id = await app.CreateNoteAsync(_setup.AdminUserId, _setup.TenantId, "v1");
        var (otherUser, otherTenant) = await app.CreateTenantAdminAsync();
        var other = app.As(otherUser, otherTenant);

        (await other.GetFromJsonAsync<EntityVersionDto[]>($"/api/v1/versions/Note/{id}", Ct))!.ShouldBeEmpty();
        (await other.PostAsync($"/api/v1/versions/Note/{id}/1/restore", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unknown_type_is_not_found() =>
        (await Admin.GetAsync($"/api/v1/versions/Nope/{Guid.CreateVersion7()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
}
