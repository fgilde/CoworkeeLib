using System.Collections;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Coworkee.Core.Diagnostics;

/// <summary>
/// Anonymous links carry their secret in the path (<c>/token/{token}</c>, <c>/downloads/{token}</c>). <see cref="AddSecretPathRedaction"/> replaces it by
/// <see cref="Mask"/> in the logs of ASP.NET Core, HttpClient and YARP (messages, values and scopes such as RequestPath) and in the activities of incoming,
/// forwarded and outgoing requests (url.path, url.full and the other url tags, display name) before any exporter sees them.
/// </summary>
public static partial class SecretPaths
{
    public const string Mask = "***";

    private static readonly string[] LoggedCategories = ["Microsoft.AspNetCore.", "System.Net.Http.HttpClient.", "Yarp."];

    private static readonly string[] TracedSources = ["Microsoft.AspNetCore", "System.Net.Http", "Yarp.ReverseProxy"];

    private static readonly string[] UrlTags = ["url.path", "url.full", "url.query", "http.target", "http.url"];

    private static readonly Lazy<ActivityListener> Listener = new(() =>
    {
        // registered before the OpenTelemetry provider (built when the host starts), so it runs first when an activity stops
        var listener = new ActivityListener
        {
            ShouldListenTo = source => TracedSources.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.None,
            ActivityStopped = Redact,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    });

    /// <summary><paramref name="text"/> with the secret segment after /token/ and /downloads/ masked.</summary>
    public static string Redact(string text) => Contains(text) ? Secret().Replace(text, Mask) : text;

    public static bool Contains(string? text) =>
        text is not null && (text.Contains("/token/", StringComparison.OrdinalIgnoreCase) || text.Contains("/downloads/", StringComparison.OrdinalIgnoreCase));

    /// <summary>Masks the secret in the url tags and the name of <paramref name="activity"/>, e.g. from an instrumentation's enrich callback.</summary>
    public static void Redact(Activity activity)
    {
        foreach (var tag in UrlTags)
        {
            if (activity.GetTagItem(tag) is { } value && Contains(value.ToString()))
            {
                activity.SetTag(tag, Redact(value.ToString()!));
            }
        }

        if (Contains(activity.DisplayName))
        {
            activity.DisplayName = Redact(activity.DisplayName);
        }
    }

    /// <summary>Wraps the logger factory (call after logging is set up, e.g. in the host's builder) and starts the activity redaction once per process.</summary>
    public static IServiceCollection AddSecretPathRedaction(this IServiceCollection services)
    {
        _ = Listener.Value;
        if (services.LastOrDefault(d => d.ServiceType == typeof(ILoggerFactory))?.ImplementationType is { } type && type != typeof(RedactingLoggerFactory))
        {
            services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(provider => new RedactingLoggerFactory((ILoggerFactory)ActivatorUtilities.CreateInstance(provider, type))));
        }

        return services;
    }

    [GeneratedRegex(@"(?<=/(?:token|downloads)/)[^/?#\s""']+", RegexOptions.IgnoreCase)]
    private static partial Regex Secret();

    private static string? Text(object? value) => value switch
    {
        null => null,
        string text => text,
        Uri uri => uri.ToString(),
        _ when value.GetType().IsValueType => value.ToString(), // PathString, QueryString
        _ => null,
    };

    /// <summary>The values of a structured state with the secrets masked, or null when there is nothing to mask.</summary>
    private static RedactedState? Redacted(object? state, Func<string> message) =>
        state is IReadOnlyList<KeyValuePair<string, object?>> values && values.Any(v => Contains(Text(v.Value)))
            ? new RedactedState([.. values.Select(v => Text(v.Value) is { } text ? new KeyValuePair<string, object?>(v.Key, Redact(text)) : v)], Redact(message()))
            : null;

    private sealed class RedactingLoggerFactory(ILoggerFactory inner) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) =>
            LoggedCategories.Any(c => categoryName.StartsWith(c, StringComparison.Ordinal)) ? new RedactingLogger(inner.CreateLogger(categoryName)) : inner.CreateLogger(categoryName);

        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

        public void Dispose() => inner.Dispose();
    }

    private sealed class RedactingLogger(ILogger inner) : ILogger
    {
        // the hosting scope (RequestPath) travels with every log record of the request, in every category
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            Redacted(state, () => state.ToString() ?? string.Empty) is { } redacted ? inner.BeginScope(redacted) : inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (Redacted(state, () => formatter(state, exception)) is { } redacted)
            {
                inner.Log(logLevel, eventId, redacted, exception, static (s, _) => s.ToString());
            }
            else if (state is not IReadOnlyList<KeyValuePair<string, object?>> && Contains(formatter(state, exception)))
            {
                inner.Log(logLevel, eventId, Redact(formatter(state, exception)), exception, static (s, _) => s);
            }
            else
            {
                inner.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }

    private sealed class RedactedState(IReadOnlyList<KeyValuePair<string, object?>> values, string message) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public KeyValuePair<string, object?> this[int index] => values[index];

        public int Count => values.Count;

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => message;
    }
}
