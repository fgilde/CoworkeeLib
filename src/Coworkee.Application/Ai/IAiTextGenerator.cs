namespace Coworkee.Application.Ai;

/// <summary>One answer of the app's configured AI model, for modules that use it without depending on the AI module (e.g. machine translation).</summary>
public interface IAiTextGenerator
{
    /// <summary>Whether a provider, model and key are configured.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    /// <returns>The model's answer to <paramref name="input"/>, null when no model is configured.</returns>
    Task<string?> GenerateAsync(string instructions, string input, CancellationToken cancellationToken);
}
