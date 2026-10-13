using System.Net.Http.Json;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Setup;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Account.Tests;

public sealed class SetupStatusRulesTests(AccountApp app)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Before_setup_the_status_carries_the_password_rules_of_the_settings()
    {
        await app.ResetAsync();
        app.App.Services.GetRequiredService<SystemStateCache>().Reset();
        await app.SetSettingAsync(PasswordSettings.RequiredLength, "14");

        var status = await Status();

        status.IsInitialized.ShouldBeFalse();
        status.PasswordRules.ShouldBe(new PasswordRules(14, 1, true, true, true, true));
    }

    [Fact]
    public async Task After_setup_the_status_carries_no_rules()
    {
        await app.SetupAsync();

        (await Status()).PasswordRules.ShouldBeNull();
    }

    private async Task<SetupStatusDto> Status() =>
        (await app.App.GetTestClient().GetFromJsonAsync<SetupStatusDto>("/api/v1/setup/status", Ct))!;
}
