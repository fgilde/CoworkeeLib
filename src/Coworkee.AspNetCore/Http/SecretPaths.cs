using System.Collections;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Coworkee.AspNetCore.Http;

/// <summary>
/// Anonymous links carry their secret in the path (<c>/token/{token}</c>, <c>/downloads/{token}</c>). The secret is replaced by <see cref="Mask"/> in the
/// request logs of ASP.NET Core and in the tags and name of the request's activity (OpenTelemetry <c>url.path</c>, <c>url.full</c>).
/// </summary>
public static partial class SecretPaths
{
    public const string Mask = "***";

    /// <summary><paramref name="text"/> with the secret segment after /token/ and /downloads/ masked.</summary>
    public static string Redact(string text) => Contains(text) ? Secret().Replace(text, Mask) : text;

    public static bool Contains(string? text) =>
        text is not null && (text.Contains("/token/", StringComparison.OrdinalIgnoreCase) || text.Contains("/downloads/", StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"(?<=/(?:token|downloads)/)[^/?#\s""']+", RegexOptions.IgnoreCase)]
    private static partial Regex Secret();

    /// <summary>Masks the secrets in the logs of the ASP.NET Core categories, whose request lines are written before any middleware runs.</summary>
    internal static IServiceCollection AddSecretPathRedaction(this IServiceCollection services)
    {
        var factory = services.LastOrDefault(d => d.ServiceType == typeof(ILoggerFactory));
        if (factory?.ImplementationType is not { } type)
        {
            return services;
        }

        services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(provider => new RedactingLoggerFactory((ILoggerFactory)ActivatorUtilities.CreateInstance(provider, type))));
        return services;
    }

    /// <summary>Masks the secret in the activity of the request, whose tags the instrumentation set from the raw path.</summary>
    internal static IApplicationBuilder UseSecretPathRedaction(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        if (Activity.Current is { } activity && Contains(context.Request.Path.Value))
        {
            activity.SetTag("url.path", Redact(context.Request.Path.Value!));
            foreach (var tag in (string[])["url.full", "http.target", "http.url"])
            {
                if (activity.GetTagItem(tag) is string value)
                {
                    activity.SetTag(tag, Redact(value));
                }
            }

            activity.DisplayName = Redact(activity.DisplayName);
        }

        return next(context);
    });

    private sealed class RedactingLoggerFactory(ILoggerFactory inner) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) =>
            categoryName.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) ? new RedactingLogger(inner.CreateLogger(categoryName)) : inner.CreateLogger(categoryName);

        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

        public void Dispose() => inner.Dispose();
    }

    private sealed class RedactingLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is not IReadOnlyList<KeyValuePair<string, object?>> values || !values.Any(v => Contains(Text(v.Value))))
            {
                inner.Log(logLevel, eventId, state, exception, formatter);
                return;
            }

            var redacted = new RedactedState(
                [.. values.Select(v => Text(v.Value) is { } text ? new KeyValuePair<string, object?>(v.Key, Redact(text)) : v)], Redact(formatter(state, exception)));
            inner.Log(logLevel, eventId, redacted, exception, static (s, _) => s.ToString());
        }

        private static string? Text(object? value) => value switch
        {
            string text => text,
            PathString path => path.Value,
            Uri uri => uri.ToString(),
            _ => null,
        };
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
