using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Configuration;
using Coworkee.Theming;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer;

/// <summary>Name, logo and colors of the account pages: the app's display name and the default theme of the system organisation.</summary>
public sealed record AuthBranding(string AppName, string? LogoDataUrl, string? Css);

public sealed partial class AuthBrandingProvider(IDispatcher dispatcher, HybridCache cache, IOptions<AuthServerOptions> options)
{
    private static readonly HybridCacheEntryOptions Entry = new() { Expiration = TimeSpan.FromMinutes(1), LocalCacheExpiration = TimeSpan.FromMinutes(1) };

    private static readonly (string Variable, string Palette)[] Variables =
        [("--paper", "Background"), ("--ink", "TextPrimary"), ("--ink-2", "TextSecondary"), ("--rule", "LinesDefault"), ("--signal", "Primary")];

    public ValueTask<AuthBranding> GetAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync("coworkee:auth-branding", LoadAsync, Entry, cancellationToken: cancellationToken);

    private async ValueTask<AuthBranding> LoadAsync(CancellationToken cancellationToken)
    {
        var theme = await dispatcher.SendAsync(new GetCurrentTheme(), cancellationToken);
        if (!theme.IsSuccess)
        {
            return new AuthBranding(options.Value.DisplayName, null, null);
        }

        var logo = theme.Value.LogoSvg is { Length: > 0 } svg ? "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg)) : null;
        var css = $":root{{{Declarations(theme.Value.PaletteLight)}}}@media (prefers-color-scheme: dark){{:root{{{Declarations(theme.Value.PaletteDark)}}}}}";
        return new AuthBranding(options.Value.DisplayName, logo, css);
    }

    // only plain color values reach the style sheet
    private static string Declarations(JsonElement palette) => string.Concat(Variables
        .Select(v => (v.Variable, Value: palette.TryGetProperty(v.Palette, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null))
        .Where(v => v.Value is not null && Color().IsMatch(v.Value))
        .Select(v => $"{v.Variable}:{v.Value};"));

    [GeneratedRegex(@"^(#[0-9a-fA-F]{3,8}|rgba?\([0-9.,%\s]+\))$")]
    private static partial Regex Color();
}
