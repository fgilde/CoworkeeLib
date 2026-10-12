using Coworkee.Application;
using Coworkee.Contracts.Localization;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Coworkee.Localization.Languages;
using Coworkee.Localization.Resources;
using Coworkee.Localization.Texts;
using Coworkee.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.Localization.MachineTranslation;

/// <summary>
/// After the start, translates what the switched on languages lack, e.g. a configured language without shipped texts or the texts of a new version.
/// Runs when <see cref="LocalizationSettings.AutoTranslate"/> is on and a translation service is configured.
/// </summary>
internal sealed partial class InitialTranslation(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, ILogger<InitialTranslation> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await StartedAsync(stoppingToken))
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            if (!await services.GetRequiredService<ISettingProvider>().GetAsync<bool>(LocalizationSettings.AutoTranslate, stoppingToken)
                || !await services.GetRequiredService<TextTranslator>().IsAvailableAsync(stoppingToken))
            {
                return;
            }

            // ponytail: one instance translates, the others skip; the next start catches up what failed
            await using var held = await services.GetRequiredService<IDistributedLock>().AcquireAsync("coworkee:localization:initial-translation", TimeSpan.Zero, stoppingToken);
            if (held is null)
            {
                return;
            }

            var translated = 0;
            foreach (var culture in await CulturesAsync(services, stoppingToken))
            {
                translated += await services.GetRequiredService<MissingTexts>().TranslateAsync(culture, stoppingToken);
            }

            if (translated > 0)
            {
                await services.GetRequiredService<LocalizationChanges>().NotifyAsync(stoppingToken);
                LogTranslated(translated);
            }
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            LogFailed(exception);
        }
    }

    private async Task<bool> StartedAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource();
        await using var registration = lifetime.ApplicationStarted.Register(started.SetResult);
        await Task.WhenAny(started.Task, Task.Delay(Timeout.Infinite, stoppingToken));
        return !stoppingToken.IsCancellationRequested;
    }

    private static async Task<IReadOnlyList<string>> CulturesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var stored = await services.GetRequiredService<CoworkeeDbContext>().Set<Language>().AsNoTracking()
            .Where(l => l.IsEnabled).Select(l => l.Culture).ToListAsync(cancellationToken);
        return stored.Count > 0
            ? stored
            : [.. InitialLanguages.Create(services.GetRequiredService<IOptions<LocalizationOptions>>().Value, services.GetRequiredService<LocalizationResources>()).Select(l => l.Culture)];
    }

    [LoggerMessage(LogLevel.Information, "Translated {Count} missing texts of the switched on languages.")]
    private partial void LogTranslated(int count);

    [LoggerMessage(LogLevel.Warning, "Translating the missing texts after the start failed.")]
    private partial void LogFailed(Exception exception);
}
