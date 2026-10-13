using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Mail;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class MailConfigurationTests : ClientTestBase
{
    private readonly IMailConnectionApi _mail = Substitute.For<IMailConnectionApi>();

    public MailConfigurationTests()
    {
        Services.AddSingleton(_mail);
        _mail.TestAsync(Arg.Any<MailConnectionTestRequest>(), Arg.Any<CancellationToken>()).Returns(new MailConnectionTestDto(true, null));
        Api.GetSettingDefinitionsAsync(false, Arg.Any<CancellationToken>()).Returns([new SettingGroupDto("Mail", "Mail",
            [new SettingDefinitionDto(MailSettings.From, "Sender address", null, SettingType.String, "noreply@localhost", [SettingScope.Global, SettingScope.Tenant], null)])]);
        Api.GetSettingsAsync(SettingScope.Global, Arg.Any<CancellationToken>()).Returns(
        [
            new SettingValueDto(MailSettings.Provider, MailProviders.SendGrid, true),
            new SettingValueDto(MailSettings.ApiKey, null, true),
            new SettingValueDto(MailSettings.From, "mail@acme.test", true),
        ]);
    }

    private IRenderedComponent<MailConfiguration> RenderPage(bool system = true) =>
        Render<MailConfiguration>(p => p.AddCascadingValue(Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(SystemTenant.ClaimType, system ? "true" : "false")], "test"))))));

    [Fact]
    public async Task Provider_choice_shows_only_the_fields_of_that_provider()
    {
        var page = RenderPage();

        page.WaitForAssertion(() => page.Find("[data-testid='mail-key-stored']"));
        page.FindAll("[data-testid='mail-host']").ShouldBeEmpty();
        page.FindAll("[data-testid='mail-domain']").ShouldBeEmpty();

        await page.Find("[data-mail-provider='smtp']").ClickAsync(new());
        page.Find("[data-testid='mail-host']");
        page.Find("[data-testid='mail-smtp-password']");
        page.FindAll("[data-testid='mail-key-stored']").ShouldBeEmpty();

        await page.Find("[data-mail-provider='mailgun']").ClickAsync(new());
        page.Find("[data-testid='mail-domain']");
        page.FindAll("[data-testid='mail-host']").ShouldBeEmpty();
        page.Find("[data-testid='mail-from']");
        page.Find("[data-testid='mail-whitelist']");
    }

    [Fact]
    public async Task System_organisation_saves_only_mail_keys_to_the_global_scope()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => page.Find("[data-testid='mail-save']"));
        page.FindAll("[data-testid='mail-own-server']").ShouldBeEmpty();

        await page.Find("[data-testid='mail-save']").ClickAsync(new());

        await Api.Received(1).SetSettingsAsync(SettingScope.Global, Arg.Is<IReadOnlyDictionary<string, string?>>(v =>
            v.Keys.All(k => k.StartsWith("Mail.", StringComparison.Ordinal)) && !v.ContainsKey(MailSettings.UseOwnServer) && !v.ContainsKey(MailSettings.ApiKey)
            && v[MailSettings.Provider] == MailProviders.SendGrid && v[MailSettings.From] == "mail@acme.test"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Test_button_sends_the_draft_and_leaves_a_stored_key_to_the_server()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => page.Find("[data-testid='mail-test']"));

        await page.Find("[data-testid='mail-test']").ClickAsync(new());

        await _mail.Received(1).TestAsync(Arg.Is<MailConnectionTestRequest>(r =>
            r.Values[MailSettings.Provider] == MailProviders.SendGrid && r.Values[MailSettings.ApiKey] == null && r.Values[MailSettings.From] == "mail@acme.test"), Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => page.Find("[data-testid='mail-test-result']").GetAttribute("data-succeeded").ShouldBe("true"));
    }

    [Fact]
    public async Task Failed_test_shows_the_error()
    {
        _mail.TestAsync(Arg.Any<MailConnectionTestRequest>(), Arg.Any<CancellationToken>()).Returns(new MailConnectionTestDto(false, "SendGrid rejected the mail with 401"));
        var page = RenderPage();
        page.WaitForAssertion(() => page.Find("[data-testid='mail-test']"));

        await page.Find("[data-testid='mail-test']").ClickAsync(new());

        page.WaitForAssertion(() => page.Find("[data-testid='mail-test-result']").TextContent.ShouldContain("401"));
    }

    [Fact]
    public async Task Organisation_without_own_server_sees_a_hint_and_saves_only_the_switch()
    {
        Api.GetSettingsAsync(SettingScope.Tenant, Arg.Any<CancellationToken>()).Returns([new SettingValueDto(MailSettings.UseOwnServer, null, false)]);
        var page = RenderPage(system: false);

        page.WaitForAssertion(() => page.Find("[data-testid='mail-own-server']"));
        page.Find("[data-testid='mail-system-server']").TextContent.ShouldContain("Mails go through the system's server.");
        page.FindAll("[data-testid='mail-from']").ShouldBeEmpty();
        page.FindAll("[data-testid='mail-test']").ShouldBeEmpty();

        await page.Find("[data-testid='mail-save']").ClickAsync(new());

        await Api.Received(1).SetSettingsAsync(SettingScope.Tenant, Arg.Is<IReadOnlyDictionary<string, string?>>(v => v.Count == 1 && v[MailSettings.UseOwnServer] == "false"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Organisation_with_own_server_edits_and_saves_its_own_values()
    {
        Api.GetSettingsAsync(SettingScope.Tenant, Arg.Any<CancellationToken>()).Returns(
        [
            new SettingValueDto(MailSettings.UseOwnServer, "true", true),
            new SettingValueDto(MailSettings.Provider, MailProviders.Mailgun, true),
            new SettingValueDto(MailSettings.MailgunDomain, "mg.acme.test", true),
            new SettingValueDto(MailSettings.ApiKey, null, true),
        ]);
        var page = RenderPage(system: false);
        page.WaitForAssertion(() => page.Find("[data-testid='mail-domain']"));
        page.FindAll("[data-testid='mail-system-server']").ShouldBeEmpty();

        await page.Find("[data-testid='mail-save']").ClickAsync(new());

        await Api.Received(1).SetSettingsAsync(SettingScope.Tenant, Arg.Is<IReadOnlyDictionary<string, string?>>(v =>
            v[MailSettings.UseOwnServer] == "true" && v[MailSettings.Provider] == MailProviders.Mailgun && v[MailSettings.MailgunDomain] == "mg.acme.test"
            && v[MailSettings.MailgunRegion] == "us" && v.Keys.All(k => k.StartsWith("Mail.", StringComparison.Ordinal))), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Form_keeps_a_stored_secret_until_one_is_typed_or_removed()
    {
        var form = MailConfigurationForm.Load([new SettingValueDto(MailSettings.ApiKey, null, true), new SettingValueDto(MailSettings.Password, null, true)], null, tenant: false);

        form.ToSettings().ShouldNotContainKey(MailSettings.ApiKey);
        form.ToSettings().ShouldNotContainKey(MailSettings.Password);
        form.NewApiKey = " typed ";
        form.RemovePassword = true;
        form.ToSettings()[MailSettings.ApiKey].ShouldBe("typed");
        form.ToSettings()[MailSettings.Password].ShouldBeNull();
        form.ToRequest().Values[MailSettings.ApiKey].ShouldBe("typed");
    }
}
