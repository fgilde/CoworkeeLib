using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Extensions.Components.ObjectEdit;
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
    public void Keeps_several_groups_open_and_offers_search_export_and_import_like_the_app_settings()
    {
        var form = Render<SettingsForm>(p => p.Add(f => f.Scope, SettingScope.Global));
        form.WaitForAssertion(() => Input(form, "Host"));

        var edit = form.FindComponent<MudExObjectEditForm<object>>().Instance;
        edit.SingleExpand.ShouldBeFalse();
        edit.StickyToolbar.ShouldBeTrue();
        edit.AllowSearch.ShouldBeTrue();
        edit.AllowExport.ShouldBeTrue();
        edit.AllowImport.ShouldBeTrue();
    }

    [Fact]
    public async Task Secrets_are_not_exported_and_an_import_leaves_them_alone()
    {
        var form = Render<SettingsForm>(p => p.Add(f => f.Scope, SettingScope.Global));
        form.WaitForAssertion(() => Input(form, "Host"));
        Input(form, "Password").Change("typed-secret");
        var edit = form.FindComponent<MudExObjectEditForm<object>>().Instance;

        var json = edit.ToJson();

        json.ShouldContain("smtp.acme.test");
        json.ShouldNotContain("typed-secret");
        json.ShouldNotContain("Mail_Smtp_Password");

        const string file = """{"Mail_Smtp_Host": "imported.test", "Mail_Smtp_Password": "injected"}""";
        await form.InvokeAsync(() => edit.LoadFromJson(file, true));
        await form.InvokeAsync(() => edit.AfterImport.InvokeAsync(new ImportedData<object> { Json = file, Value = edit.Value }));
        await form.InvokeAsync(() => form.Instance.SaveAsync());

        await Api.Received(1).SetSettingsAsync(SettingScope.Global,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v["Mail.Smtp.Host"] == "imported.test" && v["Mail.Smtp.Password"] == "typed-secret" && v.Count == 2), Arg.Any<CancellationToken>());
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

    [Theory]
    [InlineData(SettingScope.Global, "Configured default")]
    [InlineData(SettingScope.Tenant, "Inherited")]
    public async Task A_language_setting_is_a_select_of_the_languages(SettingScope scope, string emptyOption)
    {
        var form = await RenderLanguageAsync(scope, "de");

        form.WaitForAssertion(() => SelectedText(form).ShouldBe("Deutsch"));
        form.FindAll(".mud-ex-list-item-text").Select(i => i.TextContent.Trim()).ShouldBe([emptyOption, "English", "Deutsch"]);
    }

    [Theory]
    [InlineData(SettingScope.Global, "Configured default")]
    [InlineData(SettingScope.Tenant, "Inherited")]
    [InlineData(SettingScope.User, "Inherited")]
    public async Task An_unset_language_shows_the_empty_option(SettingScope scope, string emptyOption)
    {
        var form = await RenderLanguageAsync(scope, null);

        form.WaitForAssertion(() => SelectedText(form).ShouldBe(emptyOption));
    }

    [Fact]
    public async Task Choosing_the_empty_language_clears_the_stored_one()
    {
        var form = await RenderLanguageAsync(SettingScope.Tenant, "de");
        form.WaitForAssertion(() => SelectedText(form).ShouldBe("Deutsch"));

        await form.FindAll(".mud-ex-list-item").First().ClickAsync(new());
        await form.InvokeAsync(() => form.Instance.SaveAsync());

        await Api.Received(1).SetSettingsAsync(SettingScope.Tenant,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v.Count == 1 && v["Localization.Culture"] == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_language_of_a_user_is_labeled_Language()
    {
        var form = await RenderLanguageAsync(SettingScope.User, null);

        form.WaitForAssertion(() => Labels(form).ShouldContain("Language"));
        Labels(form).ShouldNotContain("Default language");
    }

    [Fact]
    public void A_choice_setting_sits_in_its_grid_column()
    {
        Api.GetSettingDefinitionsAsync(false, Arg.Any<CancellationToken>()).Returns(
        [
            new SettingGroupDto("Translation", "Translation",
            [
                new SettingDefinitionDto("Translation.Note", "Note", null, SettingType.String, null, Global, null),
                new SettingDefinitionDto("Translation.Service", "Service", "Which service translates", SettingType.Choice, "a", Global, ["a", "b"]),
            ]),
        ]);
        var form = Render<SettingsForm>(p => p.Add(f => f.Scope, SettingScope.Global));

        form.WaitForAssertion(() => Input(form, "Note"));
        var selects = form.FindAll(".mud-ex-select");
        selects.ShouldNotBeEmpty();
        selects.ShouldAllBe(s => s.Closest(".mud-grid-item") != null);
    }

    [Fact]
    public async Task A_choice_shows_the_translated_text_and_saves_the_raw_value()
    {
        Localization.GetLanguagesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns([new LanguageDto(null, "de", "Deutsch", true, true)]);
        Localization.GetTextsAsync("de", Arg.Any<CancellationToken>()).Returns(new TextsDto("de", new Dictionary<string, string> { ["Internal"] = "Nur Passwort", ["External"] = "Nur externe Anbieter" }));
        await Services.GetRequiredService<CoworkeeLocalizer>().InitializeAsync(null);
        Api.GetSettingDefinitionsAsync(false, Arg.Any<CancellationToken>()).Returns(
        [
            new SettingGroupDto("Auth", "Auth", [new SettingDefinitionDto("Auth.LoginMode", "Sign-in methods", null, SettingType.Choice, "Internal", Global, ["Internal", "External", "libreTranslate"])]),
        ]);
        Api.GetSettingsAsync(SettingScope.Global, Arg.Any<CancellationToken>()).Returns([new SettingValueDto("Auth.LoginMode", null, false)]);
        var form = Render<SettingsForm>(p => p.Add(f => f.Scope, SettingScope.Global));
        form.WaitForAssertion(() => SelectedText(form).ShouldBe("Nur Passwort"));

        await form.FindAll(".mud-ex-list-item")[1].ClickAsync(new());
        await form.InvokeAsync(() => form.Instance.SaveAsync());

        form.FindAll(".mud-ex-list-item-text").Select(i => i.TextContent.Trim()).ShouldBe(["Nur Passwort", "Nur externe Anbieter", "libreTranslate"]);
        await Api.Received(1).SetSettingsAsync(SettingScope.Global,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v.Count == 1 && v["Auth.LoginMode"] == "External"), Arg.Any<CancellationToken>());
    }

    private async Task<IRenderedComponent<SettingsForm>> RenderLanguageAsync(SettingScope scope, string? stored)
    {
        Localization.GetLanguagesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new LanguageDto(null, "en", "English", true, true), new LanguageDto(null, "de", "Deutsch", true, false)]);
        await Services.GetRequiredService<CoworkeeLocalizer>().InitializeAsync(null);
        SettingScope[] scopes = [scope];
        Api.GetSettingDefinitionsAsync(scope == SettingScope.User, Arg.Any<CancellationToken>()).Returns(
        [
            new SettingGroupDto("Localization", "Localization", [new SettingDefinitionDto("Localization.Culture", "Default language", null, SettingType.Language, null, scopes, null)]),
        ]);
        Api.GetSettingsAsync(scope, Arg.Any<CancellationToken>()).Returns([new SettingValueDto("Localization.Culture", stored, stored is not null)]);
        return Render<SettingsForm>(p => p.Add(f => f.Scope, scope));
    }

    private static string SelectedText(IRenderedComponent<SettingsForm> form) => form.Find("div.mud-input-root.mud-ex-select-input").TextContent.Trim();

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
