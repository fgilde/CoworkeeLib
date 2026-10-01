using System.Security.Cryptography;
using System.Text;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coworkee.Identity.Setup;

public sealed class SetupGateOptions
{
    public List<string> AllowedPrefixes { get; } = ["/api/v1/setup"];
}

public sealed class SetupToken
{
    public SetupToken(IConfiguration configuration)
    {
        var configured = configuration["Coworkee:SetupToken"];
        IsGenerated = string.IsNullOrWhiteSpace(configured);
        Value = IsGenerated ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_') : configured!;
    }

    internal string Value { get; }

    internal bool IsGenerated { get; }

    public bool Matches(string candidate) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Value), Encoding.UTF8.GetBytes(candidate ?? string.Empty));
}

public sealed class SystemStateCache(IServiceScopeFactory scopes)
{
    private volatile bool _initialized;

    public async Task<bool> IsInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return true;
        }

        await using var scope = scopes.CreateAsyncScope();
        _initialized = await scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<SystemState>()
            .AnyAsync(s => s.IsInitialized, cancellationToken);
        return _initialized;
    }

    internal void Reset() => _initialized = false;
}

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
