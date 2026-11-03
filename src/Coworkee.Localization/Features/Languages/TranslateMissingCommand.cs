using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Localization;
using Coworkee.Core.Results;

namespace Coworkee.Localization.Features.Languages;

[AiTool(Exclude = true)]
[RequiresPermission(LocalizationPermissions.Manage)]
public sealed record TranslateMissingCommand(string Culture) : ICommand<Result<TranslateMissingDto>>;
