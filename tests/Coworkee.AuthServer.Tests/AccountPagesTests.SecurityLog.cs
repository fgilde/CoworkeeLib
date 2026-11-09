using System.Net;
using Coworkee.Contracts.Auditing;
using Coworkee.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.AuthServer.Tests;

public sealed partial class AccountPagesTests
{
    [Fact]
    public async Task Sign_ins_are_logged_with_their_result_and_reason()
    {
        await LoginAsync(app.Browser(), "nobody@acme.test", Password);
        await LoginAsync(app.Browser(), "admin@acme.test", "wrong-password");
        (await LoginAsync(app.Browser(), "admin@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var events = await SecurityEventsAsync();
        events.Select(e => (e.Action, e.Succeeded, e.Reason)).ShouldBe(
        [
            (SecurityActions.SignIn, false, "UnknownUser"),
            (SecurityActions.SignIn, false, "InvalidCredentials"),
            (SecurityActions.SignIn, true, null),
        ]);
        events[0].ShouldSatisfyAllConditions(e => e.UserId.ShouldBeNull(), e => e.TenantId.ShouldBeNull(), e => e.Details.ShouldContain("nobody@acme.test"));
        events.Skip(1).ShouldAllBe(e => e.UserId == _setup.AdminUserId && e.TenantId == _setup.TenantId && e.Details!.Contains("password"));
    }

    [Fact]
    public async Task Failed_sign_ins_that_lock_the_account_log_the_lockout()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await LoginAsync(app.Browser(), "admin@acme.test", "wrong-password");
        }

        await LoginAsync(app.Browser(), "admin@acme.test", Password);

        var events = await SecurityEventsAsync();
        var lockout = events.Where(e => e.Action == SecurityActions.LockedOut).ShouldHaveSingleItem();
        (lockout.UserId, lockout.Reason, lockout.ActorId).ShouldBe((_setup.AdminUserId, "FailedAttempts", null));
        events.Last().ShouldSatisfyAllConditions(e => e.Action.ShouldBe(SecurityActions.SignIn), e => e.Succeeded.ShouldBeFalse(), e => e.Reason.ShouldBe("LockedOut"));
    }

    private Task<List<SecurityEvent>> SecurityEventsAsync() =>
        app.InDbAsync(db => db.Set<SecurityEvent>().AsNoTracking().Where(e => e.Action == SecurityActions.SignIn || e.Action == SecurityActions.LockedOut)
            .OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync());
}
