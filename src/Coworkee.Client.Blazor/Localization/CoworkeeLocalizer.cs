using System.Globalization;
using Coworkee.Client.Blazor.Api;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Localization;

/// <summary>
/// Texts of the current language: <c>L["Save"]</c>. Keys are the English texts, so English needs no translation and a missing
/// translation shows the English text. Keys a translation lacks are reported for the translation editor (the server takes them from signed-in users only).
/// </summary>
public sealed class CoworkeeLocalizer(ILocalizationApi api, IJSRuntime js) : IDisposable
{
    public const string English = "en";
    // only a language the user picked on this device; a chosen one is kept, an automatic one follows the user setting and the default
    internal const string StorageKey = "coworkee.culture.chosen";
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, string> _texts = new Dictionary<string, string>();
    private Timer? _flush;
    private string? _chosen;
    private string? _userCulture;
    private string? _browser;

    public string Culture { get; private set; } = English;

    /// <summary>Counts every change of the language or its texts; the layout rebuilds with it, so an edited translation shows at once.</summary>
    public int Revision { get; private set; }

    public IReadOnlyList<Contracts.Localization.LanguageDto> Languages { get; private set; } = [];

    public event Action? Changed;

    public string this[string key] => Lookup(key);

    public string this[string key, params object?[] arguments] => string.Format(CultureInfo.CurrentCulture, Lookup(key), arguments);

    /// <summary>A text from the server: translated and filled when it came with arguments (a localizable text), else as it is.</summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(text))]
    public string? Server(string? text, IReadOnlyList<string>? arguments) => text is null || arguments is null ? text : this[text, [.. arguments]];

    /// <summary>The translation of <paramref name="key"/>, or null when the language has none.</summary>
    public string? Find(string key) => _texts.TryGetValue(key, out var text) ? text : null;

    /// <summary>Text users or a seed stored (a report name, a widget title): its translation when one exists, else as it is; never reported as missing.</summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(text))]
    public string? Stored(string? text) => text is { Length: > 0 } ? Find(text) ?? text : text;

    public bool IsInitialized { get; private set; }

    /// <summary>
    /// Picks the language: the choice made on this device, then the user setting, then the browser, then the default. The first call runs before
    /// the first render; later calls bring a changed user setting (saved on another device or in the settings) live: it replaces the choice of
    /// this device.
    /// </summary>
    public async Task InitializeAsync(string? userCulture)
    {
        if (IsInitialized)
        {
            if (!string.Equals(userCulture, _userCulture, StringComparison.OrdinalIgnoreCase))
            {
                _userCulture = userCulture;
                if (IsOffered(userCulture))
                {
                    _chosen = null;
                    await TryJsAsync<object?>("localStorage.removeItem", StorageKey);
                }

                await FollowAsync();
            }

            return;
        }

        IsInitialized = true;
        _userCulture = userCulture;
        _chosen = await TryJsAsync<string?>("localStorage.getItem", StorageKey);
        try
        {
            Languages = await api.GetLanguagesAsync();
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            return;
        }

        _browser = await TryJsAsync<string?>("eval", "navigator.language");
        await ApplyAsync(Preferred());
    }

    /// <summary>The user picks <paramref name="culture"/>: kept on this device until the user setting changes.</summary>
    public async Task UseAsync(string culture)
    {
        _chosen = culture;
        await TryJsAsync<object?>("localStorage.setItem", StorageKey, culture);
        await ApplyAsync(culture);
    }

    /// <summary>
    /// After an administrator changed languages or texts: reloads both, shows edited texts at once and moves users without a choice of their own to a
    /// new default. Returns the language switched to when the current one was switched off, otherwise null.
    /// </summary>
    public async Task<string?> RefreshAsync()
    {
        try
        {
            Languages = await api.GetLanguagesAsync();
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            return null;
        }

        var switchedOff = !IsOffered(Culture);
        await ApplyAsync(Preferred());
        return switchedOff ? Culture : null;
    }

    private Task FollowAsync() => string.Equals(Preferred(), Culture, StringComparison.OrdinalIgnoreCase) ? Task.CompletedTask : ApplyAsync(Preferred());

    private string Preferred() =>
        new[] { _chosen, _userCulture, _browser, _browser?.Split('-')[0] }.FirstOrDefault(IsOffered)
        ?? Languages.FirstOrDefault(l => l.IsDefault)?.Culture ?? Languages.FirstOrDefault()?.Culture ?? English;

    private async Task ApplyAsync(string culture)
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
        var changed = culture != Culture || !Same(texts, _texts);
        (_texts, Culture) = (texts, culture);
        var info = CultureInfo.GetCultureInfo(culture);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = info;
        if (changed)
        {
            Revision++;
            Changed?.Invoke();
        }
    }

    private static bool Same(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && other == pair.Value);

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
        if (keys.Length == 0)
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
