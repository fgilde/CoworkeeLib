using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Setup;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Account.Tests;

public sealed class PasswordRulesTests(AccountApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Password_rules_come_from_the_settings()
    {
        await app.SetSettingAsync(PasswordSettings.RequiredLength, "12");
        await app.SetSettingAsync(PasswordSettings.RequireDigit, "false");

        (await ValidateAsync("abcdefghijK!")).Succeeded.ShouldBeTrue();
        (await ValidateAsync("abcdefK!")).Errors.Select(e => e.Code).ShouldBe(["PasswordTooShort"]);
    }

    [Fact]
    public async Task Required_length_is_at_least_one()
    {
        await app.SetSettingAsync(PasswordSettings.RequiredLength, "0");
        await app.SetSettingAsync(PasswordSettings.RequireDigit, "false");
        await app.SetSettingAsync(PasswordSettings.RequireLowercase, "false");
        await app.SetSettingAsync(PasswordSettings.RequireUppercase, "false");
        await app.SetSettingAsync(PasswordSettings.RequireNonAlphanumeric, "false");

        (await ValidateAsync("")).Errors.Select(e => e.Code).ShouldContain("PasswordTooShort");
        (await ValidateAsync("a")).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task A_whitespace_only_password_is_too_short()
    {
        await app.SetSettingAsync(PasswordSettings.RequiredLength, "1");
        await app.SetSettingAsync(PasswordSettings.RequireDigit, "false");
        await app.SetSettingAsync(PasswordSettings.RequireLowercase, "false");
        await app.SetSettingAsync(PasswordSettings.RequireUppercase, "false");
        await app.SetSettingAsync(PasswordSettings.RequireNonAlphanumeric, "false");

        (await ValidateAsync("   ")).Errors.Select(e => e.Code).ShouldBe(["PasswordTooShort"]);
    }

    [Fact]
    public async Task Impossible_rules_are_clamped_like_the_hint_shows_them()
    {
        await app.SetSettingAsync(PasswordSettings.RequiredLength, "500");
        await app.SetSettingAsync(PasswordSettings.RequiredUniqueChars, "900");
        await app.SetSettingAsync(PasswordSettings.RequireDigit, "false");
        await app.SetSettingAsync(PasswordSettings.RequireLowercase, "false");
        await app.SetSettingAsync(PasswordSettings.RequireUppercase, "false");
        await app.SetSettingAsync(PasswordSettings.RequireNonAlphanumeric, "false");
        var password = string.Concat(Enumerable.Range(0, 128).Select(i => (char)('!' + i)));

        (await ValidateAsync(password)).Succeeded.ShouldBeTrue();
        (await ValidateAsync(password[..127])).Errors.Select(e => e.Code).ShouldContain("PasswordTooShort");
    }

    [Fact]
    public async Task Setup_refuses_an_admin_password_that_breaks_the_stricter_rules()
    {
        await app.ResetAsync();
        app.App.Services.GetRequiredService<SystemStateCache>().Reset();
        await app.SetSettingAsync(PasswordSettings.RequiredLength, "20");

        var response = await app.App.GetTestClient().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("20");
    }

    [Fact]
    public async Task Admin_set_password_follows_the_settings()
    {
        await app.SetSettingAsync(PasswordSettings.RequiredLength, "12");
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);

        var response = await admin.PutAsJsonAsync($"/api/v1/identity/users/{_setup.AdminUserId}/password", new SetPasswordRequest("Short#1a", false), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("12");
        (await admin.PutAsJsonAsync($"/api/v1/identity/users/{_setup.AdminUserId}/password", new SetPasswordRequest("Long#Enough1a", false), Ct)).EnsureSuccessStatusCode();
    }

    private Task<IdentityResult> ValidateAsync(string password) => app.InScopeAsync(null, _setup.TenantId, async sp =>
    {
        var users = sp.GetRequiredService<UserManager<User>>();
        var results = new List<IdentityError>();
        foreach (var validator in users.PasswordValidators)
        {
            results.AddRange((await validator.ValidateAsync(users, new User(), password)).Errors);
        }

        return results.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. results]);
    });
}
