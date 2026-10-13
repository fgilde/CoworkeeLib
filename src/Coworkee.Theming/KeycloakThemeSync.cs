using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.Theming;

/// <summary>
/// Keeps the Keycloak login theme (<see cref="KeycloakThemeOptions"/>) in the design of the system organisation's default theme and the sign-in design:
/// written at start and shortly after either changes, or at once on request.
/// </summary>
internal sealed class KeycloakThemeSync : IHostedService, IDisposable
{
    internal const string Failed = "The Keycloak login theme could not be written; see the log of the API.";

    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<KeycloakThemeOptions> _options;
    private readonly IOptionsMonitor<SignInDesign> _design;
    private readonly TimeProvider _time;
    private readonly ILogger<KeycloakThemeSync> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ITimer _timer;
    private IDisposable? _designChanged;
    private DateTimeOffset? _writtenAt;
    private bool _failed;
    private volatile bool _disposed;

    public KeycloakThemeSync(IServiceScopeFactory scopes, IOptionsMonitor<KeycloakThemeOptions> options, IOptionsMonitor<SignInDesign> design, TimeProvider time, ILogger<KeycloakThemeSync> logger)
    {
        (_scopes, _options, _design, _time, _logger) = (scopes, options, design, time, logger);
        _timer = time.CreateTimer(_ => _ = WriteAsync(CancellationToken.None), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public KeycloakThemeStatusDto Status => new(Configured, _options.CurrentValue.ThemeName, _writtenAt, _failed ? Failed : null);

    private bool Configured => _options.CurrentValue is { ThemeDirectory.Length: > 0, ThemeName.Length: > 0 };

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _designChanged = _design.OnChange(_ => Schedule());
        Schedule();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Change(Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    /// <summary>Several changes in a row write once.</summary>
    public void Schedule()
    {
        if (Configured)
        {
            Change(Delay);
        }
    }

    public async Task<KeycloakThemeStatusDto> WriteAsync(CancellationToken cancellationToken)
    {
        if (!Configured || _disposed)
        {
            return Status;
        }

        try
        {
            await _gate.WaitAsync(cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            return Status;
        }

        try
        {
            var options = _options.CurrentValue;
            // the system organisation's theme, whoever asks: the request user would bring their own organisation's theme
            using var system = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
            await using var scope = _scopes.CreateAsyncScope();
            var theme = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new GetCurrentTheme(), cancellationToken);
            var design = _design.CurrentValue;
            KeycloakLoginTheme.Write(options.ThemeDirectory!, options.ThemeName!, new(
                options.AppName ?? options.ThemeName!,
                theme.IsSuccess ? Colors(theme.Value.PaletteLight) : KeycloakLoginTheme.DefaultLight,
                theme.IsSuccess ? Colors(theme.Value.PaletteDark) : KeycloakLoginTheme.DefaultDark)
            {
                Logo = theme.IsSuccess ? theme.Value.Logo : null,
                LogoUrl = OnWeb(options.LogoUrl, options),
                Design = new() { Layout = design.Layout, MediaSide = design.MediaSide, MediaUrl = OnWeb(design.MediaUrl, options) },
            });
            (_writtenAt, _failed) = (_time.GetUtcNow(), false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // the message names folders of the host: for the log only
            _failed = true;
            _logger.LogWarning(exception, "The Keycloak login theme {Theme} could not be written", _options.CurrentValue.ThemeName);
        }
        finally
        {
            _gate.Release();
        }

        return Status;
    }

    public void Dispose()
    {
        _disposed = true;
        _designChanged?.Dispose();
        _timer.Dispose();
        _gate.Dispose();
    }

    private void Change(TimeSpan due)
    {
        try
        {
            _timer.Change(due, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // a save committed while the host was shutting down
        }
    }

    private static Dictionary<string, string> Colors(JsonElement palette) => palette.ValueKind == JsonValueKind.Object
        ? palette.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()!)
        : [];

    private static string? OnWeb(string? url, KeycloakThemeOptions options) =>
        url is not null && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var web)
            ? new Uri(web, url).AbsoluteUri
            : url;
}
