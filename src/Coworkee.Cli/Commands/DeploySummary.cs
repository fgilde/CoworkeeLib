using System.Text.RegularExpressions;

namespace Coworkee.Cli.Commands;

/// <summary>What <c>coworkee deploy</c> asks for and what it prints at the end: the addresses of the services and tools, and how to sign in.</summary>
internal static partial class DeploySummary
{
    /// <summary>
    /// Values for the .env beyond those of an earlier run: an environment variable of the same name, the administrator's email, and what the
    /// person at the console types for the administrator and the mail server (<paramref name="ask"/> is null without a console).
    /// </summary>
    public static Dictionary<string, string> Known(IEnumerable<string> keys, IReadOnlyDictionary<string, string> previous, Func<string, string?> environment, string? adminEmail,
        Func<string, string?>? ask)
    {
        var known = new Dictionary<string, string>(previous);
        foreach (var key in keys.Where(k => known.GetValueOrDefault(k) is not { Length: > 0 }))
        {
            var value = environment(key) is { Length: > 0 } fromEnvironment ? fromEnvironment
                : ComposeEnv.IsAdminEmail(key) && adminEmail is { Length: > 0 } ? adminEmail
                : ask is not null && (ComposeEnv.IsAdminEmail(key) || ComposeEnv.IsSmtp(key)) ? ask(key) : null;
            if (value is { Length: > 0 })
            {
                known[key] = value;
            }
        }

        return known;
    }

    /// <summary>One line per service or tool Docker Compose binds to the loopback: its port and its public address in the .env.</summary>
    public static IEnumerable<string> Addresses(string compose, IReadOnlyDictionary<string, string> values, string appName)
    {
        string? service = null;
        foreach (var line in compose.Replace("\r\n", "\n").Split('\n'))
        {
            if (ServiceLine().Match(line) is { Success: true } name)
            {
                service = name.Groups[1].Value;
            }
            else if (service is not null && LoopbackPort().Match(line) is { Success: true } port)
            {
                var prefix = $"{appName.ToLowerInvariant()}-";
                var key = $"{(service.StartsWith(prefix, StringComparison.Ordinal) ? service : prefix + service).ToUpperInvariant().Replace('-', '_')}_URL";
                yield return $"{service,-18} 127.0.0.1:{port.Groups[1].Value,-6} {(values.ContainsKey(key) ? key + "=" : "")}{(!values.TryGetValue(key, out var url) ? "(no public address needed)" : url.Length > 0 ? url : "(not set yet)")}";
            }
        }
    }

    public static IEnumerable<string> Credentials(IReadOnlyDictionary<string, string> values)
    {
        var email = values.FirstOrDefault(v => ComposeEnv.IsAdminEmail(v.Key));
        var password = values.FirstOrDefault(v => v.Key.EndsWith("_ADMIN_PASSWORD", StringComparison.Ordinal));
        if (email.Value is { Length: > 0 } && password.Value is { Length: > 0 })
        {
            yield return $"Sign in as {email.Value} with the password {password.Value} (also in .env as {email.Key} and {password.Key}): the app, and Keycloak, Grafana, WebDataStudio, AspireUI and Mailpit when they are deployed.";
            yield return "The migrations create this administrator in an empty database only; with the values left empty the app shows its setup page.";
        }

        if (values.ContainsKey("KEYCLOAK_PASSWORD"))
        {
            yield return "Keycloak's own administration console: user admin, password KEYCLOAK_PASSWORD from .env.";
        }
    }

    /// <summary>The address parameters (<c>shop_web_url</c>, <c>shop_grafana_url</c>, ...) of a Bicep or Helm output.</summary>
    public static IReadOnlyList<string> PublicAddresses(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".bicep", StringComparison.Ordinal) || f.EndsWith("values.yaml", StringComparison.Ordinal))
            .SelectMany(f => UrlParameter().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).Distinct().Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"^  ([a-z0-9][a-z0-9-]*):\s*$")]
    private static partial Regex ServiceLine();

    [GeneratedRegex(@"127\.0\.0\.1:(\d+):")]
    private static partial Regex LoopbackPort();

    // Bicep names them shop_web_url_value
    [GeneratedRegex(@"\b([a-z0-9]+(?:_[a-z0-9]+)*?_url)(?:_value)?\b")]
    private static partial Regex UrlParameter();
}
