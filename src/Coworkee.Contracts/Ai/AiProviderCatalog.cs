namespace Coworkee.Contracts.Ai;

/// <summary>How the server talks to a provider; everything but Anthropic speaks the OpenAI chat API.</summary>
public enum AiProtocol
{
    Anthropic,
    OpenAI,
    AzureOpenAI,
    Ollama,
}

public enum AiField
{
    None,
    Optional,
    Required,
}

/// <summary>
/// One provider the assistant can use: where it answers (<see cref="BaseUrl"/>), what it needs, models to start with
/// (<see cref="Models"/>, the first is the default) and where to get a key.
/// </summary>
public sealed record AiProviderInfo(
    string Id,
    string Name,
    AiProtocol Protocol,
    string? BaseUrl,
    AiField Key,
    AiField Endpoint,
    IReadOnlyList<string> Models,
    string? KeyUrl,
    string Notes,
    string Color,
    string? Logo = null,
    bool Local = false,
    bool FreeTier = false)
{
    public string? DefaultModel => Models.Count > 0 ? Models[0] : null;

    /// <summary>Stands in for a logo.</summary>
    public string Monogram => Name[..1];

    public bool IsComplete(string? model, string? endpoint, bool hasKey) =>
        (DefaultModel is not null || !string.IsNullOrWhiteSpace(model))
        && (Key != AiField.Required || hasKey)
        && (Endpoint != AiField.Required || WebAddress.IsWeb(endpoint));
}

/// <summary>The providers the AI settings, the CLI and the Studio offer, in the order they show.</summary>
public static class AiProviderCatalog
{
    public const string Anthropic = "anthropic";
    public const string OpenAI = "openAI";
    public const string AzureOpenAI = "azureOpenAI";
    public const string Gemini = "gemini";
    public const string OpenRouter = "openRouter";
    public const string Mistral = "mistral";
    public const string Groq = "groq";
    public const string DeepSeek = "deepSeek";
    public const string XAi = "xai";
    public const string Together = "together";
    public const string Fireworks = "fireworks";
    public const string Ollama = "ollama";
    public const string LmStudio = "lmStudio";
    public const string Custom = "openAICompatible";

    public static readonly IReadOnlyList<AiProviderInfo> All =
    [
        new(Anthropic, "Anthropic", AiProtocol.Anthropic, "https://api.anthropic.com", AiField.Required, AiField.Optional,
            ["claude-opus-5-5", "claude-sonnet-4-5", "claude-haiku-4-5"], "https://console.anthropic.com/settings/keys",
            "Claude models; strong at tool use. Pay per use.", "#D97757", AiProviderLogos.Anthropic),
        new(OpenAI, "OpenAI", AiProtocol.OpenAI, "https://api.openai.com/v1", AiField.Required, AiField.Optional,
            ["gpt-5", "gpt-5-mini", "gpt-4.1"], "https://platform.openai.com/api-keys",
            "GPT models (ChatGPT). Pay per use.", "#10A37F", AiProviderLogos.OpenAI),
        new(AzureOpenAI, "Azure OpenAI", AiProtocol.AzureOpenAI, null, AiField.Required, AiField.Required,
            [], "https://portal.azure.com/#view/Microsoft_Azure_ProjectOxford/CognitiveServicesHub/~/OpenAI",
            "OpenAI models in your Azure tenant. Endpoint: https://<resource>.openai.azure.com, model: the deployment name.", "#0078D4"),
        new(Gemini, "Google Gemini", AiProtocol.OpenAI, "https://generativelanguage.googleapis.com/v1beta/openai", AiField.Required, AiField.Optional,
            ["gemini-2.5-flash", "gemini-2.5-pro"], "https://aistudio.google.com/apikey",
            "Gemini through Google's OpenAI compatible API. Free tier with rate limits.", "#8E75B2", AiProviderLogos.Gemini, FreeTier: true),
        new(OpenRouter, "OpenRouter", AiProtocol.OpenAI, "https://openrouter.ai/api/v1", AiField.Required, AiField.Optional,
            ["anthropic/claude-sonnet-4.5", "openai/gpt-5", "google/gemini-2.5-pro"], "https://openrouter.ai/keys",
            "Hundreds of models behind one key; models ending in :free cost nothing. Optional headers HTTP-Referer and X-Title name your app.",
            "#6467F2", AiProviderLogos.OpenRouter, FreeTier: true),
        new(Mistral, "Mistral", AiProtocol.OpenAI, "https://api.mistral.ai/v1", AiField.Required, AiField.Optional,
            ["mistral-large-latest", "mistral-medium-latest", "mistral-small-latest"], "https://console.mistral.ai/api-keys",
            "European provider, hosted in the EU. Free experiment plan.", "#FA520F", AiProviderLogos.Mistral, FreeTier: true),
        new(Groq, "Groq", AiProtocol.OpenAI, "https://api.groq.com/openai/v1", AiField.Required, AiField.Optional,
            ["openai/gpt-oss-120b", "llama-3.3-70b-versatile"], "https://console.groq.com/keys",
            "Open models at very high speed. Free tier with rate limits.", "#F55036", FreeTier: true),
        new(DeepSeek, "DeepSeek", AiProtocol.OpenAI, "https://api.deepseek.com/v1", AiField.Required, AiField.Optional,
            ["deepseek-chat", "deepseek-reasoner"], "https://platform.deepseek.com/api_keys",
            "Low prices; servers in China.", "#4D6BFE", AiProviderLogos.DeepSeek),
        new(XAi, "xAI Grok", AiProtocol.OpenAI, "https://api.x.ai/v1", AiField.Required, AiField.Optional,
            ["grok-4", "grok-3-mini"], "https://console.x.ai",
            "Grok models. Pay per use.", "#111111"),
        new(Together, "Together AI", AiProtocol.OpenAI, "https://api.together.xyz/v1", AiField.Required, AiField.Optional,
            ["meta-llama/Llama-3.3-70B-Instruct-Turbo", "Qwen/Qwen3-235B-A22B-Instruct-2507-tput"], "https://api.together.ai/settings/api-keys",
            "Open models in the cloud. Pay per use.", "#0F6FFF"),
        new(Fireworks, "Fireworks AI", AiProtocol.OpenAI, "https://api.fireworks.ai/inference/v1", AiField.Required, AiField.Optional,
            ["accounts/fireworks/models/llama-v3p3-70b-instruct", "accounts/fireworks/models/qwen3-235b-a22b"], "https://fireworks.ai/account/api-keys",
            "Open models in the cloud. Pay per use.", "#5019C5"),
        new(Ollama, "Ollama", AiProtocol.Ollama, "http://localhost:11434", AiField.None, AiField.Optional,
            ["llama3.2", "qwen3", "mistral-small3.2"], "https://ollama.com/search",
            "Local models, no key and no data leaves your machine. Small models call tools less reliably.", "#000000", AiProviderLogos.Ollama, Local: true),
        new(LmStudio, "LM Studio", AiProtocol.OpenAI, "http://localhost:1234/v1", AiField.None, AiField.Optional,
            [], "https://lmstudio.ai",
            "Local models from the LM Studio app; start its server and load a model. No key, nothing leaves your machine.",
            "#4338CA", AiProviderLogos.LmStudio, Local: true),
        new(Custom, "Custom", AiProtocol.OpenAI, null, AiField.Optional, AiField.Required,
            [], null,
            "Any server that speaks the OpenAI chat API: a gateway, vLLM, LiteLLM, a company proxy. Base address, key, model and headers as it needs.",
            "#64748B"),
    ];

    public static IReadOnlyList<string> Ids { get; } = [.. All.Select(p => p.Id)];

    public static AiProviderInfo? Find(string? id) => All.FirstOrDefault(p => p.Id == id);
}
