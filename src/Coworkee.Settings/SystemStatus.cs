using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Application.Status;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coworkee.Settings;

/// <summary>The status of the installation, only in the system organisation: versions, database, jobs, mail and what the modules add.</summary>
[RequiresPermission(SettingsPermissions.Manage)]
public sealed record GetSystemStatus : IQuery<Result<SystemStatusDto>>;

internal sealed partial class SystemStatusHandler(
    IEnumerable<ISystemStatusContributor> contributors, ICurrentUser currentUser, IServiceProvider services, TimeProvider time, ILogger<SystemStatusHandler> logger)
    : IHandler<GetSystemStatus, Result<SystemStatusDto>>
{
    private static readonly Error SystemOnly = Error.Forbidden("system_status.system_only", "The status of the installation is shown in the system organisation.");

    public async Task<Result<SystemStatusDto>> HandleAsync(GetSystemStatus query, CancellationToken cancellationToken)
    {
        if (!await currentUser.IsInSystemTenantAsync(services, cancellationToken))
        {
            return SystemOnly;
        }

        // one after the other: the sections share the request's DbContext
        var sections = new List<SystemStatusSection>();
        foreach (var contributor in contributors)
        {
            try
            {
                sections.Add(await contributor.GetAsync(cancellationToken));
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogFailed(exception, contributor.GetType().Name);
                sections.Add(new SystemStatusSection(contributor.GetType().Name, "Status unavailable",
                    [SystemStatusEntry.Text("Status", "Unavailable", SystemStatusLevel.Error)], [new SystemStatusProblem(contributor.GetType().Name, exception.Message)], 1000));
            }
        }

        return new SystemStatusDto(time.GetUtcNow(), [.. sections.OrderBy(s => s.Order)]);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The status section {Contributor} failed.")]
    private partial void LogFailed(Exception exception, string contributor);
}
