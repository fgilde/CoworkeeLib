using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Settings;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SettingsFormTests : ClientTestBase
{
    private static readonly SettingScope[] Global = [SettingScope.Global];

    public SettingsFormTests()
    {
        Api.GetSettingDefinitionsAsync(false, Arg.Any<CancellationToken>()).Returns(
        [
            new SettingGroupDto("Mail", "Mail",
            [
                new SettingDefinitionDto("Mail.Smtp.Host", "Host", null, SettingType.String, null, Global, null),
                new SettingDefinitionDto("Mail.Smtp.Port", "Port", null, SettingType.Int, "25", Global, null),
                new SettingDefinitionDto("Mail.Smtp.UseSsl", "SSL", null, SettingType.Bool, "false", Global, null),
                new SettingDefinitionDto("Mail.Smtp.Password", "Password", null, SettingType.Secret, null, Global, null),
                new SettingDefinitionDto("Other.TenantOnly", "Tenant only", null, SettingType.String, null, [SettingScope.Tenant], null),
            ]),
        ]);
        Api.GetSettingsAsync(SettingScope.Global, Arg.Any<CancellationToken>()).Returns(
        [
            new SettingValueDto("Mail.Smtp.Host", "smtp.acme.test", true),
            new SettingValueDto("Mail.Smtp.Port", null, false),
            new SettingValueDto("Mail.Smtp.UseSsl", null, false),
            new SettingValueDto("Mail.Smtp.Password", null, true),
        ]);
    }

    [Fact]
    public void Renders_inputs_by_type_for_the_scope()
    {
        var form = Render<SettingsForm>(p => p.Add(f => f.Scope, SettingScope.Global));

        form.WaitForElement("[data-setting='Mail.Smtp.Host'] input").GetAttribute("value").ShouldBe("smtp.acme.test");
        form.Find("[data-setting='Mail.Smtp.Port'] input").GetAttribute("type").ShouldBe("number");
        form.Find("[data-setting='Mail.Smtp.UseSsl'] input").GetAttribute("type").ShouldBe("checkbox");
        form.Find("[data-setting='Mail.Smtp.Password'] input").GetAttribute("type").ShouldBe("password");
        form.FindAll("[data-setting='Other.TenantOnly']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Saves_only_changed_values()
    {
        var form = Render<SettingsForm>(p => p.Add(f => f.Scope, SettingScope.Global));
        form.WaitForElement("[data-setting='Mail.Smtp.Port'] input").Change("2525");

        await form.Find("[data-testid='save-settings']").ClickAsync(new());

        await Api.Received(1).SetSettingsAsync(SettingScope.Global,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v.Count == 1 && v["Mail.Smtp.Port"] == "2525"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stored_secret_shows_a_placeholder_and_is_sent_only_when_typed()
    {
        var form = Render<SettingsForm>(p => p.Add(f => f.Scope, SettingScope.Global));
        var secret = form.WaitForElement("[data-setting='Mail.Smtp.Password'] input");
        secret.GetAttribute("value").ShouldBeNullOrEmpty();
        form.Find("[data-setting='Mail.Smtp.Password']").TextContent.ShouldContain("stored");

        form.Find("[data-setting='Mail.Smtp.Host'] input").Change("smtp2.acme.test");
        await form.Find("[data-testid='save-settings']").ClickAsync(new());

        await Api.Received(1).SetSettingsAsync(SettingScope.Global,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => !v.ContainsKey("Mail.Smtp.Password")), Arg.Any<CancellationToken>());
    }
}
