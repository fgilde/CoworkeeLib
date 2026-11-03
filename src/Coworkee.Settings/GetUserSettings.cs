using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;

namespace Coworkee.Settings;

public sealed record GetUserSettings : IQuery<Result<IReadOnlyList<SettingValueDto>>>;
