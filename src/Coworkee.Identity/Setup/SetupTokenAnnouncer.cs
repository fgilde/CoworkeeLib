using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coworkee.Identity.Setup;

internal sealed partial class SetupTokenAnnouncer(SystemStateCache state, SetupToken token, ILogger<SetupTokenAnnouncer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (token.IsGenerated && !await state.IsInitializedAsync(stoppingToken))
            {
                LogToken(token.Value);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogUnavailable(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "System is not set up yet. Setup token: {Token}")]
    private partial void LogToken(string token);

    [LoggerMessage(Level = LogLevel.Information, Message = "Setup state not available yet")]
    private partial void LogUnavailable(Exception exception);
}
