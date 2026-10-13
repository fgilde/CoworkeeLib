using Coworkee.Contracts.Configuration;
using Coworkee.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Account.Tests;

public sealed class LoginModeSettingTests(AccountApp app)
{
    [Fact]
    public void Login_mode_is_not_defined_without_an_external_provider() =>
        app.App.Services.GetRequiredService<ISettingDefinitionManager>().Find(AuthSettings.LoginMode).ShouldBeNull();
}
