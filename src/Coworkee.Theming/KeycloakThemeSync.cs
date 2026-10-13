using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Theming;
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
    private string? _error;

    public KeycloakThemeSync(IServiceScopeFactory scopes, IOptionsMonitor<KeycloakThemeOptions> options, IOptionsMonitor<SignInDesign> design, TimeProvider time, ILogger<KeycloakThemeSync> logger)
    {
        (_scopes, _options, _design, _time, _logger) = (scopes, options, design, time, logger);
        _timer = time.CreateTimer(_ => _ = WriteAsync(CancellationToken.None), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public KeycloakThemeStatusDto Status => new(Configured, _options.CurrentValue.ThemeName, _writtenAt, _error);

    private bool Configured => _options.CurrentValue is { ThemeDirectory.Length: > 0, ThemeName.Length: > 0 };

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _designChanged = _design.OnChange(_ => Schedule());
        Schedule();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    /// <summary>Several changes in a row write once; the delay also lets the change that asked for it be saved first.</summary>
    public void Schedule()
    {
        if (Configured)
        {
            _timer.Change(Delay, Timeout.InfiniteTimeSpan);
        }
    }

    public async Task<KeycloakThemeStatusDto> WriteAsync(CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return Status;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var options = _options.CurrentValue;
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
            (_writtenAt, _error) = (_time.GetUtcNow(), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _error = exception.Message;
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
        _designChanged?.Dispose();
        _timer.Dispose();
        _gate.Dispose();
    }

    private static Dictionary<string, string> Colors(JsonElement palette) => palette.ValueKind == JsonValueKind.Object
        ? palette.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()!)
        : [];

    private static string? OnWeb(string? url, KeycloakThemeOptions options) =>
        url is not null && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var web)
            ? new Uri(web, url).AbsoluteUri
            : url;
}
