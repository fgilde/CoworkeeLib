using System.ComponentModel;
using Coworkee.Contracts.Auditing;
using Coworkee.Settings;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Auditing.Requests;

/// <summary>The request log settings as one cached value: read on every request, so it lives in the hybrid cache under the settings tag, which a saved setting clears on all instances.</summary>
[ImmutableObject(true)]
internal sealed record RequestLogPolicy(bool Reading, bool Writing, int MaxBodyBytes, string[] RedactedNames)
{
    public const string CacheKey = "coworkee:request-log";

    /// <summary>Names whose values never reach the log, in headers, query strings and bodies; a name matches any name that contains it (case, - and _ aside).</summary>
    public static readonly string[] AlwaysRedacted = ["authorization", "cookie", "password", "secret", "token", "apikey", "credential"];

    public static ValueTask<RequestLogPolicy> GetAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<HybridCache>().GetOrCreateAsync(CacheKey, services, static async (provider, token) =>
        {
            var settings = provider.GetRequiredService<ISettingProvider>();
            var requests = SettingDefinition.SplitChoices(await settings.GetAsync(RequestLogSettings.Requests, token));
            var kilobytes = await settings.GetAsync<int?>(RequestLogSettings.MaxBodyKb, token) ?? 32;
            var names = SettingDefinition.SplitChoices(await settings.GetAsync(RequestLogSettings.RedactedNames, token));
            return new RequestLogPolicy(requests.Contains(RequestLogSettings.Reading), requests.Contains(RequestLogSettings.Writing),
                Math.Clamp(kilobytes, 0, RequestLogSettingDefinitions.MaxBodyKb) * 1024, [.. AlwaysRedacted, .. names.Select(Normalize)]);
        }, tags: [ISettingProvider.CacheTag], cancellationToken: cancellationToken);

    public bool Redacts(string name)
    {
        var normalized = Normalize(name);
        return RedactedNames.Any(n => n.Length > 0 && normalized.Contains(n, StringComparison.Ordinal));
    }

    private static string Normalize(string name) => name.Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}
