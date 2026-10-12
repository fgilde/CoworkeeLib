using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;

namespace Coworkee.Ai.Tests;

public sealed class ConnectionEndpointTests(AiApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task The_stored_key_stays_hidden_and_only_reaches_the_provider_it_was_saved_for()
    {
        (await Admin.PutAsJsonAsync("/api/v1/settings/global", new SetSettingsRequest(new Dictionary<string, string?>
        {
            [AiSettingNames.Provider] = AiProviderCatalog.OpenRouter,
            [AiSettingNames.ApiKey] = "sk-or-stored",
            [AiSettingNames.Headers] = "X-Title: Acme",
        }), Ct)).EnsureSuccessStatusCode();
        var values = (await Admin.GetFromJsonAsync<SettingValueDto[]>("/api/v1/settings/global", Ct))!;
        values.Single(v => v.Name == AiSettingNames.ApiKey).ShouldBe(new SettingValueDto(AiSettingNames.ApiKey, null, true));
        app.Claude.Models = """{"data":[{"id":"openai/gpt-5"},{"id":"anthropic/claude-sonnet-4.5"}]}""";

        var listed = await ModelsAsync(new AiConnectionDraft(AiProviderCatalog.OpenRouter, Headers: "X-Title: Acme"));

        listed.Models.Select(m => m.Id).ShouldBe(["anthropic/claude-sonnet-4.5", "openai/gpt-5"]);
        var call = app.Claude.Calls.Single();
        call.Uri.ToString().ShouldBe("https://openrouter.ai/api/v1/models");
        call.Headers["Authorization"].ShouldBe("Bearer sk-or-stored");
        call.Headers["X-Title"].ShouldBe("Acme");

        app.Claude.Calls.Clear();
        await ModelsAsync(new AiConnectionDraft(AiProviderCatalog.OpenRouter, Endpoint: "https://elsewhere.test/v1"));
        app.Claude.Calls.Single().Headers.ShouldNotContainKey("Authorization");

        app.Claude.Calls.Clear();
        await ModelsAsync(new AiConnectionDraft(AiProviderCatalog.OpenRouter, ApiKey: "sk-typed"));
        app.Claude.Calls.Single().Headers["Authorization"].ShouldBe("Bearer sk-typed");
    }

    [Fact]
    public async Task Testing_a_connection_answers_with_the_result_and_bad_drafts_are_rejected()
    {
        app.Claude.Text("OK");
        app.Claude.ToolUse(("ping", new() { ["text"] = "pong" }));

        var result = await (await Admin.PostAsJsonAsync("/api/v1/ai/connection/test", new AiConnectionDraft(AiProviderCatalog.Anthropic), Ct))
            .Content.ReadFromJsonAsync<AiConnectionTestDto>(Ct);

        (result!.Succeeded, result.Reply, result.CallsTools).ShouldBe((true, "OK", true));
        app.Claude.Calls.First().Headers["x-api-key"].ShouldBe("test-key");

        (await Admin.PostAsJsonAsync("/api/v1/ai/connection/test", new AiConnectionDraft("nope"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/ai/connection/models", new AiConnectionDraft(AiProviderCatalog.Custom, Endpoint: "file:///etc"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/ai/connection/models", new AiConnectionDraft(AiProviderCatalog.Custom, Headers: "broken"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var incomplete = await (await Admin.PostAsJsonAsync("/api/v1/ai/connection/test", new AiConnectionDraft(AiProviderCatalog.AzureOpenAI), Ct))
            .Content.ReadFromJsonAsync<AiConnectionTestDto>(Ct);
        incomplete!.Succeeded.ShouldBeFalse();
    }

    private async Task<AiModelListDto> ModelsAsync(AiConnectionDraft draft)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/ai/connection/models", draft, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AiModelListDto>(Ct))!;
    }
}
