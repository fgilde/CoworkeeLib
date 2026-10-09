using System.Globalization;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>A text per language: one field per culture, languages of the app to pick, other culture codes typed in.</summary>
public partial class TranslationsEditor
{
    private string? _input;
    private string? _error;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public IReadOnlyDictionary<string, string>? Value { get; set; }

    [Parameter] public EventCallback<IReadOnlyDictionary<string, string>> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? HelperText { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    private IReadOnlyDictionary<string, string> Texts => Value ?? new Dictionary<string, string>();

    private string LanguageName(string culture) =>
        L.Languages.FirstOrDefault(l => string.Equals(l.Culture, culture, StringComparison.OrdinalIgnoreCase))?.Name ?? Culture(culture)?.NativeName ?? culture;

    private Task<IEnumerable<string>> SearchAsync(string? text, CancellationToken cancellationToken)
    {
        var open = L.Languages.Select(l => l.Culture).Where(c => !Texts.ContainsKey(c));
        if (!string.IsNullOrWhiteSpace(text))
        {
            open = open.Where(c => c.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase) || LanguageName(c).Contains(text.Trim(), StringComparison.CurrentCultureIgnoreCase));
        }

        return Task.FromResult(open);
    }

    private Task SelectAsync(string? value)
    {
        _input = value;
        _error = null;
        return L.Languages.Any(l => l.Culture == value) ? AddAsync(value!) : Task.CompletedTask;
    }

    private async Task KeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await AddInputAsync();
        }
    }

    private async Task AddInputAsync()
    {
        if (string.IsNullOrWhiteSpace(_input))
        {
            return;
        }

        if (Culture(_input.Trim()) is not { Name.Length: > 0 } culture)
        {
            _error = L["Not a language code like de or fr-CA"];
            return;
        }

        await AddAsync(culture.Name);
    }

    private Task AddAsync(string culture)
    {
        _input = null;
        _error = null;
        return Texts.ContainsKey(culture) ? Task.CompletedTask : SetAsync(culture, string.Empty);
    }

    private Task SetAsync(string culture, string? text) => ChangeAsync(texts => texts[culture] = text ?? string.Empty);

    private Task RemoveAsync(string culture) => ChangeAsync(texts => texts.Remove(culture));

    private async Task ChangeAsync(Action<Dictionary<string, string>> change)
    {
        var texts = new Dictionary<string, string>(Texts, StringComparer.OrdinalIgnoreCase);
        change(texts);
        Value = texts;
        await ValueChanged.InvokeAsync(texts);
    }

    private static CultureInfo? Culture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
