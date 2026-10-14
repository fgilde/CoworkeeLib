using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace Coworkee.Bff;

/// <summary>
/// What the app remembered in this browser for the next loading screen (cookie <see cref="Cookie"/>, written by the app's layout): colour mode,
/// chosen theme and language. Without it the language follows Accept-Language. Only hints: the theme is checked by the API.
/// </summary>
public sealed partial record LoadingScreenPreferences(string? Mode, Guid? ThemeId, string Culture)
{
    public const string Cookie = "coworkee-loading";

    public static LoadingScreenPreferences Read(HttpRequest request)
    {
        var values = request.Cookies.TryGetValue(Cookie, out var cookie) && cookie.Length <= 512 ? QueryHelpers.ParseQuery(cookie) : [];
        var mode = values.TryGetValue("mode", out var m) && m.ToString() is "light" or "dark" or "system" ? m.ToString() : null;
        var theme = values.TryGetValue("theme", out var t) && Guid.TryParse(t, out var id) ? id : (Guid?)null;
        var culture = values.TryGetValue("culture", out var c) && CultureName().IsMatch(c.ToString())
            ? c.ToString()
            : request.GetTypedHeaders().AcceptLanguage.OrderByDescending(l => l.Quality ?? 1).Select(l => l.Value.Value).FirstOrDefault(l => l is not null && CultureName().IsMatch(l));
        return new LoadingScreenPreferences(mode, theme, culture ?? "en");
    }

    [GeneratedRegex("^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8}){0,2}$")]
    private static partial Regex CultureName();
}
