using System.Globalization;
using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Localization;

/// <summary>
/// Texts of the current language: <c>L["Save"]</c>. Keys are the English texts, so English needs no translation and a missing
/// translation shows the English text. Keys a translation lacks are reported (signed-in users only) for the translation editor.
/// </summary>
public sealed class CoworkeeLocalizer(ILocalizationApi api, IJSRuntime js, AuthenticationStateProvider? authentication = null) : IDisposable
{
    public const string English = "en";
    private const string StorageKey = "coworkee.culture";
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, string> _texts = new Dictionary<string, string>();
    private Timer? _flush;

    public string Culture { get; private set; } = English;

    public IReadOnlyList<Contracts.Localization.LanguageDto> Languages { get; private set; } = [];

    public event Action? Changed;

    public string this[string key] => Lookup(key);

    public string this[string key, params object?[] arguments] => string.Format(CultureInfo.CurrentCulture, Lookup(key), arguments);

    /// <summary>The translation of <paramref name="key"/>, or null when the language has none.</summary>
    public string? Find(string key) => _texts.TryGetValue(key, out var text) ? text : null;

    /// <summary>Picks the language: the stored choice, then the user setting, then the browser, then the default.</summary>
    public async Task InitializeAsync(string? userCulture)
    {
        try
        {
            Languages = await api.GetLanguagesAsync();
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            return;
        }

        var stored = await TryJsAsync<string?>("localStorage.getItem", StorageKey);
        var browser = await TryJsAsync<string?>("eval", "navigator.language");
        var culture = new[] { stored, userCulture, browser, browser?.Split('-')[0] }.FirstOrDefault(IsOffered)
            ?? Languages.FirstOrDefault(l => l.IsDefault)?.Culture ?? English;
        await UseAsync(culture);
    }

    public async Task UseAsync(string culture)
    {
        IReadOnlyDictionary<string, string> texts;
        try
        {
            texts = (await api.GetTextsAsync(culture)).Texts;
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            texts = new Dictionary<string, string>();
        }

        // re-rendering the whole layout resets inputs being typed into, so only when something changed
        var changed = culture != Culture || texts.Count > 0 || _texts.Count > 0;
        (_texts, Culture) = (texts, culture);
        var info = CultureInfo.GetCultureInfo(culture);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = info;
        await TryJsAsync<object?>("localStorage.setItem", StorageKey, culture);
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public void Dispose() => _flush?.Dispose();

    private bool IsOffered(string? culture) => culture is { Length: > 0 } && Languages.Any(l => string.Equals(l.Culture, culture, StringComparison.OrdinalIgnoreCase));

    private string Lookup(string key)
    {
        if (_texts.TryGetValue(key, out var text))
        {
            return text;
        }

        if (Culture != English && _reported.Add(key))
        {
            lock (_missing)
            {
                _missing.Add(key);
            }

            _flush ??= new Timer(_ => _ = FlushAsync(), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        }

        return key;
    }

    private async Task FlushAsync()
    {
        string[] keys;
        lock (_missing)
        {
            keys = [.. _missing.Take(200)];
            _missing.ExceptWith(keys);
        }

        _flush?.Dispose();
        _flush = null;
        if (keys.Length == 0 || authentication is null || (await authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        try
        {
            await api.ReportMissingAsync(keys);
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
        }
    }

    private async Task<T?> TryJsAsync<T>(string identifier, params object?[] arguments)
    {
        try
        {
            return await js.InvokeAsync<T>(identifier, arguments);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException)
        {
            return default;
        }
    }
}
