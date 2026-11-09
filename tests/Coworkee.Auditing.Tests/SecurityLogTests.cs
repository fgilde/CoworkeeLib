using System.Net;
using System.Text.Json;
using Coworkee.Application.Auditing;
using Coworkee.Application.Privacy;
using Coworkee.Contracts.Auditing;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Auditing.Tests;

public sealed class SecurityLogTests(AuditApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Credential_and_access_changes_are_logged_with_who_made_them()
    {
        var bob = await app.CreateUserAsync(_setup.TenantId);
        var editor = await app.AsActorAsync(null, null, db => db.Set<Role>().IgnoreQueryFilters().Select(r => new { r.Id, r.Name }).FirstAsync());

        await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async db =>
        {
            var user = await db.Set<User>().SingleAsync(u => u.Id == bob);
            user.LockoutEnd = DateTimeOffset.UtcNow.AddDays(30);
            user.PasswordHash = "changed";
            user.Email = "bob@new.test";
            db.Add(new IdentityUserRole<Guid> { UserId = bob, RoleId = editor.Id });
            db.Add(new PermissionGrant { TenantId = _setup.TenantId, Name = AuditPermissions.SecurityLog, ProviderType = PermissionProviderType.User, ProviderKey = bob });
            return await db.SaveChangesAsync();
        });
        await app.AsActorAsync(bob, _setup.TenantId, async db =>
        {
            (await db.Set<User>().SingleAsync(u => u.Id == bob)).LockoutEnd = null;
            return await db.SaveChangesAsync();
        });

        var events = (await app.SecurityEventsAsync()).Where(e => e.UserId == bob).ToList();
        events.Select(e => e.Action).ShouldBe(
            [SecurityActions.PasswordChanged, SecurityActions.EmailChanged, SecurityActions.LockedOut, SecurityActions.RoleAssigned, SecurityActions.PermissionGranted, SecurityActions.Unlocked],
            ignoreOrder: true);
        events.Where(e => e.Action != SecurityActions.Unlocked).ShouldAllBe(e => e.ActorId == _setup.AdminUserId && e.TenantId == _setup.TenantId && e.Succeeded);
        events.Single(e => e.Action == SecurityActions.Unlocked).ActorId.ShouldBeNull("the user himself");
        events.Single(e => e.Action == SecurityActions.LockedOut).Reason.ShouldBe("Administrator");
        events.Single(e => e.Action == SecurityActions.RoleAssigned).Details.ShouldContain(editor.Name!);
        events.Single(e => e.Action == SecurityActions.PermissionGranted).Details.ShouldContain(AuditPermissions.SecurityLog);
        events.Single(e => e.Action == SecurityActions.EmailChanged).Details.ShouldContain("bob@new.test");
    }

    [Fact]
    public async Task Code_writes_entries_of_its_own_and_the_list_is_per_tenant()
    {
        var (otherUser, otherTenant) = await app.CreateTenantAdminAsync();
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            var log = scope.ServiceProvider.GetRequiredService<ISecurityLog>();
            await log.WriteAsync(new SecurityLogEntry(SecurityActions.SessionEnded, _setup.AdminUserId, Reason: "mine", Details: new { session = "s1" }), Ct);
            await log.WriteAsync(new SecurityLogEntry(SecurityActions.SessionEnded, otherUser, Reason: "theirs"), Ct);
            await log.WriteAsync(new SecurityLogEntry(SecurityActions.SignIn, Succeeded: false, Reason: "anonymous"), Ct);
        }

        var stored = await app.SecurityEventsAsync();
        stored.Single(e => e.Reason == "theirs").TenantId.ShouldBe(otherTenant, "looked up from the user");
        var list = await Admin.GetStringAsync("/odata/SecurityEvents?$top=1000", Ct);

        list.ShouldContain("mine");
        list.ShouldContain("anonymous", Case.Sensitive, "the system organisation sees entries without one");
        list.ShouldNotContain("theirs");
        var theirs = await app.As(otherUser, otherTenant).GetStringAsync("/odata/SecurityEvents?$top=1000", Ct);
        theirs.ShouldContain("theirs");
        theirs.ShouldNotContain("mine");
        theirs.ShouldNotContain("anonymous");
        (await app.As(await app.CreateUserAsync(_setup.TenantId), _setup.TenantId).GetAsync("/odata/SecurityEvents", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await app.As(await app.CreateUserAsync(_setup.TenantId, AuditPermissions.SecurityLog), _setup.TenantId).GetAsync("/odata/SecurityEvents", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_export_has_the_entries_and_erasure_keeps_them_without_the_user()
    {
        var bob = await app.CreateUserAsync(_setup.TenantId);
        await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async db =>
        {
            (await db.Set<User>().SingleAsync(u => u.Id == bob)).PasswordHash = "changed";
            return await db.SaveChangesAsync();
        });
        await using var scope = app.App.Services.CreateAsyncScope();
        var privacy = scope.ServiceProvider.GetServices<IPersonalDataContributor>().Single(c => c.Section == "securityLog");

        JsonSerializer.Serialize(await privacy.ExportAsync(new PersonalDataSubject(bob, _setup.TenantId, null), Ct)).ShouldContain(SecurityActions.PasswordChanged);
        await privacy.EraseAsync(new PersonalDataSubject(_setup.AdminUserId, _setup.TenantId, null), Ct);

        var entry = (await app.SecurityEventsAsync()).Single(e => e.UserId == bob);
        (entry.ActorId, entry.Ip, entry.UserAgent, entry.Details).ShouldBe((PersonalDataSubject.ErasedUserId, null, null, null));
        entry.Action.ShouldBe(SecurityActions.PasswordChanged);
    }
}
