namespace Coworkee.Contracts.Ai;

/// <summary>How the server talks to a provider; everything but Anthropic speaks the OpenAI chat API.</summary>
public enum AiProtocol
{
    Anthropic,
    OpenAI,
    AzureOpenAI,
    Ollama,
}
