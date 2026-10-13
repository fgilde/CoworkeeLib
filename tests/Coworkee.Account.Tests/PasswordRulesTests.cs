using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
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
