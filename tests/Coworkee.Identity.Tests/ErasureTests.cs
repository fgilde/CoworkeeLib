using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Privacy;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Tokens;
using Coworkee.Infrastructure.Versioning;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Tests;

/// <summary>What an erase does to the records that stay: bookkeeping columns, audit trail, snapshots, outbox, links; and what the export lists of identity.</summary>
public sealed class ErasureTests(IdentityApp app) : IAsyncLifetime
{
    private static readonly Guid Erased = PersonalDataSubject.ErasedUserId;

    private SetupResultDto _setup = null!;
    private Guid _bob;
    private Guid _carl;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
        _bob = await CreateAsync("bob@acme.test");
        _carl = await CreateAsync("carl@acme.test");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Guid Tenant => _setup.TenantId;

    [Fact]
    public async Task The_export_lists_claims_own_grants_resource_grants_and_selected_groups()
    {
        var team = await AddGroupAsync(new ImpersonatedUser(null, Tenant), "Team");
        await app.InDbAsync(Tenant, async db =>
        {
            var role = await db.Set<Role>().FirstAsync(r => r.Name == SystemRoles.User, Ct);
            db.Add(new IdentityUserClaim<Guid> { UserId = _bob, ClaimType = "badge", ClaimValue = "42" });
            db.Add(new PermissionGrant { TenantId = Tenant, Name = IdentityPermissions.Users.View, ProviderType = PermissionProviderType.User, ProviderKey = _bob });
            db.Add(new ResourcePermission { TenantId = Tenant, ResourceType = "Folder", ResourceId = Guid.CreateVersion7(), PrincipalType = PrincipalType.User, PrincipalId = _bob, RoleId = role.Id });
            db.Add(new GroupSelection { UserId = _bob, GroupId = team });
            return 0;
        });

        var profile = (await app.As(_bob, Tenant).GetFromJsonAsync<JsonElement>("/api/v1/identity/me/personal-data", Ct)).GetProperty("profile");

        profile.GetProperty("claims")[0].GetProperty("claimValue").GetString().ShouldBe("42");
        profile.GetProperty("permissionGrants")[0].GetString().ShouldBe(IdentityPermissions.Users.View);
        profile.GetProperty("resourcePermissions")[0].GetProperty("resourceType").GetString().ShouldBe("Folder");
        profile.GetProperty("selectedGroups")[0].GetString().ShouldBe("Team");
    }

    [Fact]
    public async Task Erasing_puts_the_tombstone_into_the_bookkeeping_columns_of_every_organisation_and_leaves_others_alone()
    {
        var bobs = await AddGroupAsync(new ImpersonatedUser(_bob, Tenant), "Bob's team");
        var carls = await AddGroupAsync(new ImpersonatedUser(_carl, Tenant), "Carl's team");
        var elsewhere = await AddGroupAsync(new ImpersonatedUser(_bob, Guid.CreateVersion7()), "Host work");

        await EraseBobAsync();

        var groups = await app.InDbAsync(null, db => db.Set<UserGroup>().IgnoreQueryFilters().ToDictionaryAsync(g => g.Id, Ct));
        groups[bobs].CreatedBy.ShouldBe(Erased);
        groups[elsewhere].CreatedBy.ShouldBe(Erased);
        groups[carls].CreatedBy.ShouldBe(_carl);
    }

    [Fact]
    public async Task Erasing_keeps_the_audit_trail_without_him_and_blanks_what_he_created_and_is_gone()
    {
        var kept = await AddGroupAsync(new ImpersonatedUser(_bob, Tenant), "Kept by Bob");
        var gone = await AddGroupAsync(new ImpersonatedUser(_bob, Tenant), "Bob's secret project");
        await app.InDbAsAsync(new ImpersonatedUser(_bob, Tenant), async db =>
        {
            db.Remove(await db.Set<UserGroup>().SingleAsync(g => g.Id == gone, Ct));
            return 0;
        });
        var mentioning = await AddGroupAsync(new ImpersonatedUser(_carl, Tenant), "Carl's", $"Ask bob@ACME.test or {_bob}");
        var impersonated = await AddGroupAsync(new ImpersonatedUser(_carl, Tenant, _bob), "Made as Carl");

        await EraseBobAsync();

        var trail = await app.InDbAsync(null, db => db.Set<AuditEntry>().ToListAsync(Ct));
        trail.ShouldNotContain(e => e.ActorId == _bob || e.ImpersonatorId == _bob);
        var keptEntry = trail.Single(e => e.EntityId == kept.ToString());
        keptEntry.ActorId.ShouldBe(Erased);
        Value(keptEntry, nameof(UserGroup.Name)).ShouldBe("\"Kept by Bob\"");
        trail.Where(e => e.EntityId == gone.ToString()).Select(e => e.Action).ShouldBe([AuditAction.Created, AuditAction.Deleted], ignoreOrder: true);
        trail.Where(e => e.EntityId == gone.ToString()).SelectMany(e => e.Changes).ShouldAllBe(c => c.OldValue == null || c.OldValue == "\"***\"");
        trail.Where(e => e.EntityId == gone.ToString()).SelectMany(e => e.Changes).ShouldAllBe(c => c.NewValue == null || c.NewValue == "\"***\"");
        var mention = trail.Single(e => e.EntityId == mentioning.ToString());
        mention.ActorId.ShouldBe(_carl);
        Value(mention, nameof(UserGroup.Description)).ShouldBe($"\"Ask *** or {Erased}\"");
        var asCarl = trail.Single(e => e.EntityId == impersonated.ToString());
        (asCarl.ActorId, asCarl.ImpersonatorId).ShouldBe((_carl, Erased));
        Value(asCarl, nameof(UserGroup.Name)).ShouldBe("\"Made as Carl\"");
    }

    [Fact]
    public async Task Erasing_cleans_snapshots_outbox_and_his_links()
    {
        var kept = await AddGroupAsync(new ImpersonatedUser(_bob, Tenant), "Versioned");
        var gone = await AddGroupAsync(new ImpersonatedUser(_bob, Tenant), "Gone");
        await app.InDbAsAsync(new ImpersonatedUser(_bob, Tenant), async db =>
        {
            db.Remove(await db.Set<UserGroup>().SingleAsync(g => g.Id == gone, Ct));
            return 0;
        });
        await app.InDbAsync(Tenant, db =>
        {
            db.Add(new EntitySnapshot { EntityType = nameof(UserGroup), EntityId = kept.ToString(), Revision = 1, Payload = $"{{\"ModifiedBy\":\"{_bob}\"}}", CreatedBy = _bob, TenantId = Tenant });
            db.Add(new EntitySnapshot { EntityType = nameof(UserGroup), EntityId = gone.ToString(), Revision = 1, Payload = "{\"Name\":\"Gone\"}", CreatedBy = _bob, TenantId = Tenant });
            db.Add(new OutboxMessage { Type = "x", Payload = "{}", ActorId = _bob, ImpersonatorId = _bob, TenantId = Tenant });
            db.Add(new OutboxMessage { Type = "y", Payload = "{}", ActorId = _carl, TenantId = Tenant });
            db.Add(new AccessToken { Hash = "bob", Purpose = "file", UserId = _bob, TenantId = Tenant, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            db.Add(new AccessToken { Hash = "carl", Purpose = "file", UserId = _carl, TenantId = Tenant, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            return Task.FromResult(0);
        });

        await EraseBobAsync();

        var snapshots = await app.InDbAsync(null, db => db.Set<EntitySnapshot>().ToListAsync(Ct));
        snapshots.ShouldNotContain(s => s.EntityId == gone.ToString());
        var snapshot = snapshots.Single(s => s.EntityId == kept.ToString());
        snapshot.CreatedBy.ShouldBe(Erased);
        JsonDocument.Parse(snapshot.Payload).RootElement.GetProperty("ModifiedBy").GetGuid().ShouldBe(Erased);
        // the setup wrote one of its own (TenantSeedRequested)
        var outbox = await app.InDbAsync(null, db => db.Set<OutboxMessage>().Where(m => m.Type == "x" || m.Type == "y").OrderBy(m => m.Type).ToListAsync(Ct));
        outbox.Select(m => (m.ActorId, m.ImpersonatorId)).ShouldBe([(Erased, Erased), (_carl, null)]);
        (await app.InDbAsync(null, db => db.Set<AccessToken>().Select(t => t.Hash).ToListAsync(Ct))).ShouldBe(["carl"]);
    }

    [Fact]
    public async Task Erasing_keeps_the_revocation_of_keys_he_revoked_without_his_id()
    {
        await app.InDbAsync(Tenant, db =>
        {
            db.Add(new ApiKeys.ApiKey { TenantId = Tenant, UserId = _carl, Name = "ci", Prefix = "cw_1", SecretHash = "x", RevokedAt = DateTimeOffset.UtcNow, RevokedBy = _bob });
            db.Add(new ApiKeys.ApiKey { TenantId = Tenant, UserId = _bob, Name = "mine", Prefix = "cw_2", SecretHash = "y" });
            return Task.FromResult(0);
        });

        await EraseBobAsync();

        var keys = await app.InDbAsync(null, db => db.Set<ApiKeys.ApiKey>().IgnoreQueryFilters().ToListAsync(Ct));
        keys.Select(k => (k.Name, k.RevokedBy)).ShouldBe([("ci", (Guid?)Erased)]);
    }

    [Fact]
    public async Task Erasing_drops_his_cached_session_stamp_so_his_tokens_stop_at_once()
    {
        var stamps = app.App.Services.GetRequiredService<Users.SessionStamps>();
        (await stamps.GetAsync(_bob, Ct)).SecurityStamp.ShouldNotBeNull();

        await EraseBobAsync();

        (await stamps.GetAsync(_bob, Ct)).SecurityStamp.ShouldBeNull();
    }

    private static string? Value(AuditEntry entry, string property) => entry.Changes.Single(c => c.Property == property).NewValue;

    private async Task EraseBobAsync() =>
        (await app.As(_setup.AdminUserId, Tenant).DeleteAsync($"/api/v1/identity/users/{_bob}", Ct)).EnsureSuccessStatusCode();

    private Task<Guid> AddGroupAsync(ImpersonatedUser user, string name, string? description = null) => app.InDbAsAsync(user, db =>
    {
        var group = new UserGroup { Name = name, Description = description, TenantId = user.TenantId!.Value, Code = Guid.NewGuid().ToString("N")[..5] };
        db.Add(group);
        return Task.FromResult(group.Id);
    });

    private async Task<Guid> CreateAsync(string email) =>
        (await (await app.As(_setup.AdminUserId, Tenant).PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
}
