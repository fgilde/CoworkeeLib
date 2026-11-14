using System.Reflection;
using Coworkee.Contracts.Configuration;
using Coworkee.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Account.Tests;

public sealed class LoginModeSettingTests(AccountApp app)
{
    [Fact]
    public void Login_mode_is_not_defined_without_an_external_provider() =>
        app.App.Services.GetRequiredService<ISettingDefinitionManager>().Find(AuthSettings.LoginMode).ShouldBeNull();

    [Theory]
    [InlineData(null, null)]
    [InlineData("External", "External")]
    public void The_operators_override_becomes_the_fixed_value_of_the_setting_without_any_secret(string? modeOverride, string? expected)
    {
        var values = new Dictionary<string, string?>
        {
            ["Coworkee:Auth:External:Mode"] = "Both",
            ["Coworkee:Auth:External:Providers:keycloak:DisplayName"] = "Keycloak",
            ["Coworkee:Auth:External:Providers:keycloak:ClientSecret"] = "s3cret",
            ["Coworkee:Auth:External:ModeOverride"] = modeOverride,
        };
        var context = new SettingDefinitionContext();

        new LoginModeSettingDefinitions(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).Define(context);

        var definition = ((List<SettingDefinition>)typeof(SettingDefinitionContext).GetProperty("Definitions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(context)!).Single();
        definition.FixedValue.ShouldBe(expected);
        definition.DefaultValue.ShouldBe("Both");
        definition.ToString().ShouldNotContain("s3cret");
    }
}
