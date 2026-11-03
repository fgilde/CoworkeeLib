using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;

namespace Coworkee.Settings;

[RequiresPermission(SettingsPermissions.Manage)]
public sealed record GetManagedSettings(SettingScope Scope) : IQuery<Result<IReadOnlyList<SettingValueDto>>>;
