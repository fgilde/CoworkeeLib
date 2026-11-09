using System.Text.RegularExpressions;

namespace Coworkee.AuthServer;

/// <summary>Device and browser of a session for people to tell their sessions apart: a rough reading of the user agent, which is kept in full as well.</summary>
internal static class UserAgents
{
    private static readonly (string Token, string Name)[] Devices =
        [("iPhone", "iPhone"), ("iPad", "iPad"), ("Android", "Android"), ("CrOS", "ChromeOS"), ("Windows", "Windows"), ("Mac OS X", "macOS"), ("Linux", "Linux")];

    // the order matters: Edge and Opera name Chrome too, Chrome names Safari
    private static readonly (string Name, Regex Version)[] Browsers =
    [
        ("Edge", new(@"Edg[A-Za-z]*/(\d+)")), ("Opera", new(@"OPR/(\d+)")), ("Firefox", new(@"(?:Firefox|FxiOS)/(\d+)")),
        ("Chrome", new(@"(?:Chrome|CriOS)/(\d+)")), ("Safari", new(@"Version/(\d+).*Safari")),
    ];

    public static (string? Device, string? Browser) Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return (null, null);
        }

        var device = Devices.FirstOrDefault(d => userAgent.Contains(d.Token, StringComparison.Ordinal)).Name;
        var browser = Browsers.Select(b => (b.Name, Match: b.Version.Match(userAgent))).FirstOrDefault(b => b.Match.Success);
        return (device, browser.Match is { } match ? $"{browser.Name} {match.Groups[1].Value}" : null);
    }
}
