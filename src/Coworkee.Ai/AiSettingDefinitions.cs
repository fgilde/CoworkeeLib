using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Settings;
using Coworkee.Settings;

namespace Coworkee.Ai;

internal sealed class AiSettingDefinitions : ISettingDefinitionContributor
{
    // global only: tenants must not switch on the assistant, pick a model or raise limits on the host's key
    private static readonly SettingScope[] Scopes = [SettingScope.Global];

    public void Define(SettingDefinitionContext context) =>
        context.Group("Ai", "AI assistant", page: "/admin/ai-settings")
            .Add(AiSettings.Enabled, "Assistant enabled", SettingType.Bool, Scopes, "false")
            .Add(AiSettings.Display, "Assistant display", SettingType.Choice, Scopes, AssistantDisplay.BottomRight, visibleToClient: true, choices: AssistantDisplay.All,
                description: "Where users find the assistant: a floating button, the app bar, the navigation, or only Ctrl+I.")
            .Add(AiSettings.Provider, "Provider", SettingType.Choice, Scopes, AiProviders.Anthropic, choices: [.. AiProviders.All])
            .Add(AiSettings.Model, "Model", SettingType.String, Scopes, description: "Empty: the provider's default. Azure OpenAI: the deployment name.")
            .Add(AiSettings.Endpoint, "Endpoint", SettingType.String, Scopes,
                description: "Empty: the provider's address. Azure OpenAI resource, Ollama or LM Studio address, or the base address of an OpenAI compatible server.")
            .Add(AiSettings.ApiKey, "API key", SettingType.Secret, Scopes)
            .Add(AiSettings.Headers, "Extra headers", SettingType.Text, Scopes, description: "One \"Name: value\" per line, for example HTTP-Referer and X-Title for OpenRouter.")
            .Add(AiSettings.AuthHeader, "Key header", SettingType.String, Scopes, description: "Empty: the key goes as bearer token. Otherwise the header that carries it, for example api-key.")
            .Add(AiSettings.Temperature, "Temperature", SettingType.String, Scopes, description: "Empty: the model's default. 0 to 2, lower answers more predictably.")
            .Add(AiSettings.MaxOutputTokens, "Maximum output tokens", SettingType.Int, Scopes, description: "Empty: the provider's default (16000 for Anthropic).")
            .Add(AiSettings.MaxToolRounds, "Maximum tool rounds per message", SettingType.Int, Scopes, "10")
            .Add(AiSettings.McpEnabled, "MCP server enabled", SettingType.Bool, Scopes, "true",
                description: "Off: MCP clients get no tools and run nothing, also with valid keys. Needs no AI provider.");
}
