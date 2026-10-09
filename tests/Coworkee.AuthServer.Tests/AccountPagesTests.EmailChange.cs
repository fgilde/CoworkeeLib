using System.Net;
using Coworkee.Account;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.Tests;

public sealed partial class AccountPagesTests
{
    [Fact]
    public async Task The_email_change_link_switches_the_address_and_the_user_name_once()
    {
        await SendEmailChangeAsync("ada@acme.test");
        var link = LocalPath(app.Mails.LinkFor("Identity.ChangeEmail", "confirm_url"));

        var changed = await app.Browser().GetStringAsync(link, Ct);
        var again = await app.Browser().GetStringAsync(link, Ct);

        changed.ShouldContain("Your account now uses ada@acme.test.");
        again.ShouldContain("The link is invalid or has expired.");
        (await LoginAsync(app.Browser(), "ada@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await LoginAsync(app.Browser(), "admin@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_email_change_link_for_another_address_is_rejected()
    {
        await SendEmailChangeAsync("ada@acme.test");
        var link = LocalPath(app.Mails.LinkFor("Identity.ChangeEmail", "confirm_url")).Replace("ada%40acme.test", "eve%40evil.test", StringComparison.Ordinal);

        (await app.Browser().GetStringAsync(link, Ct)).ShouldContain("The link is invalid or has expired.");
        (await LoginAsync(app.Browser(), "admin@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task An_email_change_to_an_address_that_may_not_sign_in_is_rejected()
    {
        await SendEmailChangeAsync("ada@other.test");
        var link = LocalPath(app.Mails.LinkFor("Identity.ChangeEmail", "confirm_url"));

        (await app.Browser().GetStringAsync(link, Ct)).ShouldContain("The link is invalid or has expired.");
        (await LoginAsync(app.Browser(), "admin@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    private async Task SendEmailChangeAsync(string email)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var admin = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(_setup.AdminUserId.ToString());
        await scope.ServiceProvider.GetRequiredService<IAccountMailer>().SendEmailChangeAsync(admin!, email, Ct);
    }
}
