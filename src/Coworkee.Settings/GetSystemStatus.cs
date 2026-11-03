using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;

namespace Coworkee.Settings;

/// <summary>The status of the installation, only in the system organisation: versions, database, jobs, mail and what the modules add.</summary>
[RequiresPermission(SettingsPermissions.Manage)]
public sealed record GetSystemStatus : IQuery<Result<SystemStatusDto>>;
