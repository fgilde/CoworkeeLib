using Coworkee.Contracts.Ai;

namespace Coworkee.Ai;

/// <summary>Ids of the common providers; <see cref="AiProviderCatalog"/> has all of them with their addresses and models.</summary>
public static class AiProviders
{
    public const string Anthropic = AiProviderCatalog.Anthropic;
    public const string OpenAI = AiProviderCatalog.OpenAI;
    public const string AzureOpenAI = AiProviderCatalog.AzureOpenAI;
    public const string Ollama = AiProviderCatalog.Ollama;
    public const string OpenAICompatible = AiProviderCatalog.Custom;

    public const string OllamaEndpoint = "http://localhost:11434";

    public static IReadOnlyList<string> All => AiProviderCatalog.Ids;

    /// <summary>The model when the setting is empty; Azure deployments and custom endpoints have no default.</summary>
    public static string? DefaultModel(string provider) => AiProviderCatalog.Find(provider)?.DefaultModel;
}
