namespace Coworkee.Bff;

/// <summary>The loading screen's words; the keys are the English texts of the app's localization (served by <c>/api/v1/localization/texts/{culture}</c>).</summary>
public sealed record LoadingScreenTexts(string Culture, string Loading, string Title, string Message, string Reload, string Details)
{
    private const string LoadingKey = "Loading";
    private const string TitleKey = "The app could not start.";
    private const string MessageKey = "Check your connection and reload the page. If it keeps happening, contact your administrator.";
    private const string ReloadKey = "Reload";
    private const string DetailsKey = "Details";

    public static LoadingScreenTexts English { get; } = new("en", LoadingKey, TitleKey, MessageKey, ReloadKey, DetailsKey);

    /// <summary>The translations found in <paramref name="texts"/>, English for the missing ones.</summary>
    public static LoadingScreenTexts From(string culture, IReadOnlyDictionary<string, string> texts)
    {
        string Text(string key) => texts.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text) ? text : key;
        return new LoadingScreenTexts(culture, Text(LoadingKey), Text(TitleKey), Text(MessageKey), Text(ReloadKey), Text(DetailsKey));
    }

    public bool IsTranslated => Title != TitleKey;
}
