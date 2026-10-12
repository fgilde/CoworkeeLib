using Bunit;
using Coworkee.Client.Blazor.Ai;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class AiConfigurationTests : ClientTestBase
{
    private readonly IAiConnectionApi _connections = Substitute.For<IAiConnectionApi>();

    public AiConfigurationTests()
    {
        Services.AddSingleton(_connections);
        Api.GetSettingsAsync(SettingScope.Global, Arg.Any<CancellationToken>()).Returns(
        [
            new SettingValueDto(AiSettingNames.Enabled, "true", true),
            new SettingValueDto(AiSettingNames.Provider, AiProviderCatalog.OpenAI, true),
            new SettingValueDto(AiSettingNames.Model, "gpt-5-mini", true),
            new SettingValueDto(AiSettingNames.ApiKey, null, true),
            new SettingValueDto(AiSettingNames.Endpoint, null, false),
        ]);
    }

    [Fact]
    public void Shows_every_provider_the_active_status_and_the_stored_key_only_masked()
    {
        var page = Render<AiConfiguration>();

        page.WaitForAssertion(() => page.Find("[data-testid='ai-status']").GetAttribute("data-status").ShouldBe("active"));
        page.FindAll("[data-provider]").Select(p => p.GetAttribute("data-provider")).ShouldBe(AiProviderCatalog.Ids);
        page.Find("[data-provider='openAI']").GetAttribute("aria-pressed").ShouldBe("true");
        page.Find("[data-provider='openAI'] [data-provider-logo] svg path").GetAttribute("d").ShouldBe(AiProviderLogos.OpenAI);
        page.Find("[data-testid='ai-key-stored']");
        page.FindAll("[data-testid='ai-key']").ShouldBeEmpty();
        page.Find("[data-testid='ai-key-link']").GetAttribute("href").ShouldBe("https://platform.openai.com/api-keys");
    }

    [Fact]
    public async Task Another_provider_needs_its_own_key_and_saving_drops_the_old_one()
    {
        var page = Render<AiConfiguration>();
        page.WaitForAssertion(() => page.Find("[data-testid='ai-key-stored']"));

        await page.Find("[data-provider='openRouter']").ClickAsync(new());

        page.Find("[data-testid='ai-key']");
        page.Markup.ShouldContain("previous provider");
        await page.Find("[data-testid='ai-save'] ").ClickAsync(new());
        await Api.Received(1).SetSettingsAsync(SettingScope.Global, Arg.Is<IReadOnlyDictionary<string, string?>>(v =>
            v[AiSettingNames.Provider] == AiProviderCatalog.OpenRouter && v.ContainsKey(AiSettingNames.ApiKey) && v[AiSettingNames.ApiKey] == null && v[AiSettingNames.Model] == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Local_providers_need_no_key_and_models_and_tests_use_the_draft()
    {
        _connections.ListModelsAsync(Arg.Any<AiConnectionDraft>(), Arg.Any<CancellationToken>())
            .Returns(new AiModelListDto([new AiModelDto("llama3.2:latest"), new AiModelDto("qwen3:8b")]));
        _connections.TestAsync(Arg.Any<AiConnectionDraft>(), Arg.Any<CancellationToken>()).Returns(new AiConnectionTestDto(true, 420, "OK", false, "did not call the test tool"));
        var page = Render<AiConfiguration>();
        page.WaitForAssertion(() => page.Find("[data-provider='ollama']"));

        await page.Find("[data-provider='ollama']").ClickAsync(new());
        page.Find("[data-testid='ai-key-none']");
        await page.Find("[data-testid='ai-load-models']").ClickAsync(new());
        await page.Find("[data-testid='ai-test']").ClickAsync(new());

        await _connections.Received(1).ListModelsAsync(new AiConnectionDraft(AiProviderCatalog.Ollama, null, null, string.Empty), Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => page.Markup.ShouldContain("2 models loaded"));
        var result = page.Find("[data-testid='ai-test-result']");
        result.GetAttribute("data-succeeded").ShouldBe("true");
        result.TextContent.ShouldContain("420 ms");
        result.ClassList.ShouldContain("mud-alert-text-warning");
    }

    [Fact]
    public void The_form_keeps_the_stored_key_only_for_its_provider_and_address()
    {
        var form = AiConnectionForm.From([new SettingValueDto(AiSettingNames.Provider, "azureOpenAI", true),
            new SettingValueDto(AiSettingNames.Endpoint, "https://acme.openai.azure.com", true), new SettingValueDto(AiSettingNames.ApiKey, null, true)]);
        form.Model = "gpt-5-deployment";

        (form.KeepsStoredKey, form.IsComplete, form.ToDraft().ApiKey).ShouldBe((true, true, null));
        form.ToSettings().ShouldNotContainKey(AiSettingNames.ApiKey);

        form.Endpoint = "https://other.openai.azure.com";
        (form.KeepsStoredKey, form.DropsStoredKey, form.IsComplete, form.ToDraft().ApiKey).ShouldBe((false, true, false, string.Empty));
        form.ToSettings()[AiSettingNames.ApiKey].ShouldBeNull();

        form.NewKey = " new-key ";
        (form.IsComplete, form.ToDraft().ApiKey, form.ToSettings()[AiSettingNames.ApiKey]).ShouldBe((true, "new-key", "new-key"));
    }
}
