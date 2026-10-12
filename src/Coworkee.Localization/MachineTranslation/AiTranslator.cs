using System.Globalization;
using System.Text.Json;
using Coworkee.Application.Ai;
using Coworkee.Contracts.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Localization.MachineTranslation;

/// <summary>The model of the AI assistant (module Coworkee.Ai); without that module the service is not available.</summary>
internal sealed class AiTranslator(IServiceProvider services) : ITranslationProvider
{
    private const string Instructions =
        """
        You translate the user interface texts of a business application. The user sends a JSON array of strings.
        Answer with a JSON array of the translations in the same order and nothing else.
        Keep placeholders like {0} or {1:N2}, HTML tags and line breaks unchanged. Use the usual software terms of the target language.
        """;

    public string Name => TranslationProviders.Ai;

    public (int Texts, int Characters) BatchLimit => (40, 8_000);

    public async Task<bool> IsConfiguredAsync(TranslationConnection connection, CancellationToken cancellationToken) =>
        services.GetService<IAiTextGenerator>() is { } generator && await generator.IsAvailableAsync(cancellationToken);

    public async Task<IReadOnlyList<string?>> TranslateAsync(IReadOnlyList<string> texts, string from, string to, TranslationConnection connection, CancellationToken cancellationToken)
    {
        var generator = services.GetRequiredService<IAiTextGenerator>();
        var instructions = $"{Instructions}\nTranslate from {LanguageName(from)} to {LanguageName(to)}.";
        var answer = await generator.GenerateAsync(instructions, JsonSerializer.Serialize(texts), cancellationToken);
        return Parse(answer, texts.Count);
    }

    // models like to wrap the array in a code fence
    internal static IReadOnlyList<string?> Parse(string? answer, int count)
    {
        var start = answer?.IndexOf('[', StringComparison.Ordinal) ?? -1;
        var end = answer?.LastIndexOf(']') ?? -1;
        try
        {
            var translations = start >= 0 && end > start ? JsonSerializer.Deserialize<string?[]>(answer![start..(end + 1)]) : null;
            return translations?.Length == count ? translations : new string?[count];
        }
        catch (JsonException)
        {
            return new string?[count];
        }
    }

    private static string LanguageName(string culture) => $"{CultureInfo.GetCultureInfo(culture).EnglishName} ({culture})";
}
