using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;

namespace Coworkee.Settings;

[Coworkee.Application.Messaging.AiTool(Exclude = true)]
[RequiresPermission(SettingsPermissions.Manage)]
public sealed record SetManagedSettings(SettingScope Scope, IReadOnlyDictionary<string, string?> Values) : ICommand<Result>;
