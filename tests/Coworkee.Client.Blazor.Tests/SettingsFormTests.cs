using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
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
    public void Renders_every_setting_of_the_scope_with_an_editor_by_type()
    {
        var form = Render<SettingsForm>(p => p.Add(f => f.Scope, SettingScope.Global));

        form.WaitForAssertion(() => Input(form, "Host").GetAttribute("value").ShouldBe("smtp.acme.test"));
        Input(form, "Port").GetAttribute("placeholder").ShouldBe("25");
        Input(form, "SSL").GetAttribute("type").ShouldBe("checkbox");
        Input(form, "Password").GetAttribute("type").ShouldBe("password");
        form.Find(".mud-expand-panel-text").TextContent.ShouldBe("Mail");
        Labels(form).ShouldNotContain("Tenant only");
    }

    [Fact]
    public async Task Loads_once_while_the_scope_stays()
    {
        var form = Render<SettingsForm>(p => p.Add(f => f.Scope, SettingScope.Global));
        form.WaitForAssertion(() => Input(form, "Host"));

        form.Render(p => p.Add(f => f.Scope, SettingScope.Global));
        form.Render(p => p.Add(f => f.Scope, SettingScope.Global));

        await Api.Received(1).GetSettingsAsync(SettingScope.Global, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Saves_only_changed_values()
    {
        var form = Render<SettingsForm>(p => p.Add(f => f.Scope, SettingScope.Global));
        form.WaitForAssertion(() => Input(form, "Port"));
        Input(form, "Port").Change("2525");

        await form.InvokeAsync(() => form.Instance.SaveAsync());

        await Api.Received(1).SetSettingsAsync(SettingScope.Global,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v.Count == 1 && v["Mail.Smtp.Port"] == "2525"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stored_secret_shows_a_hint_and_is_sent_only_when_typed()
    {
        var form = Render<SettingsForm>(p => p.Add(f => f.Scope, SettingScope.Global));
        form.WaitForAssertion(() => Input(form, "Password").GetAttribute("value").ShouldBeNullOrEmpty());
        form.Markup.ShouldContain("A value is stored");

        Input(form, "Host").Change("smtp2.acme.test");
        await form.InvokeAsync(() => form.Instance.SaveAsync());

        await Api.Received(1).SetSettingsAsync(SettingScope.Global,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v.Count == 1 && v["Mail.Smtp.Host"] == "smtp2.acme.test"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_setting_is_disabled_while_its_condition_fails()
    {
        Api.GetSettingDefinitionsAsync(false, Arg.Any<CancellationToken>()).Returns(
        [
            new SettingGroupDto("Feature", "Feature",
            [
                new SettingDefinitionDto("Feature.On", "On", null, SettingType.Bool, "false", Global, null),
                new SettingDefinitionDto("Feature.Key", "Key", null, SettingType.String, null, Global, null, SettingCondition.IsTrue("Feature.On")),
            ]),
        ]);
        var form = Render<SettingsForm>(p => p.Add(f => f.Scope, SettingScope.Global));
        form.WaitForAssertion(() => Input(form, "Key").HasAttribute("disabled").ShouldBeTrue());

        Input(form, "On").Change(true);

        form.WaitForAssertion(() => Input(form, "Key").HasAttribute("disabled").ShouldBeFalse());
    }

    [Fact]
    public void A_condition_on_a_missing_setting_keeps_the_field_enabled()
    {
        Api.GetSettingDefinitionsAsync(false, Arg.Any<CancellationToken>()).Returns(
        [
            new SettingGroupDto("Feature", "Feature",
            [
                new SettingDefinitionDto("Feature.Key", "Key", null, SettingType.String, null, Global, null, SettingCondition.IsTrue("Feature.On")),
            ]),
        ]);
        var form = Render<SettingsForm>(p => p.Add(f => f.Scope, SettingScope.Global));

        form.WaitForAssertion(() => Input(form, "Key").HasAttribute("disabled").ShouldBeFalse());
    }

    [Fact]
    public void Groups_with_an_own_page_are_not_shown()
    {
        Api.GetSettingDefinitionsAsync(false, Arg.Any<CancellationToken>()).Returns(
        [
            new SettingGroupDto("Mail", "Mail", [new SettingDefinitionDto("Mail.Smtp.Host", "Host", null, SettingType.String, null, Global, null)]),
            new SettingGroupDto("Ai", "AI", [new SettingDefinitionDto("Ai.Model", "Model", null, SettingType.String, null, Global, null)], "/admin/ai-settings"),
        ]);
        var form = Render<SettingsForm>(p => p.Add(f => f.Scope, SettingScope.Global));

        form.WaitForAssertion(() => Input(form, "Host"));
        Labels(form).ShouldNotContain("Model");
    }

    [Fact]
    public void A_setting_name_becomes_a_property_name()
    {
        SettingsModel.PropertyName("Mail.Smtp.Host").ShouldBe("Mail_Smtp_Host");
        SettingsModel.PropertyName("Ai.Open-Ai.Key").ShouldBe("Ai_Open_Ai_Key");
    }

    /// <summary>The input of the field labeled <paramref name="label"/>: text fields point at it, a check box wraps it.</summary>
    internal static AngleSharp.Dom.IElement Input(IRenderedComponent<IComponent> cut, string label)
    {
        var element = cut.FindAll("label").First(l => l.TextContent.Trim() == label);
        return element.GetAttribute("for") is { } id ? cut.Find($"#{id}") : element.QuerySelector("input")!;
    }

    internal static IReadOnlyList<string> Labels(IRenderedComponent<IComponent> cut) => [.. cut.FindAll("label").Select(l => l.TextContent.Trim())];
}
