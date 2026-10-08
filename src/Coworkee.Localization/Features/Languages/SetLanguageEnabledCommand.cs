using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Localization;
using Coworkee.Core.Results;

namespace Coworkee.Localization.Features.Languages;

/// <summary>Switches a culture on or off; switching on translates its missing texts when a translator is configured.</summary>
[AiTool(Exclude = true)]
[RequiresPermission(LocalizationPermissions.Manage)]
public sealed record SetLanguageEnabledCommand(string Culture, bool Enabled) : ICommand<Result<LanguageSwitchDto>>;

[AiTool(Exclude = true)]
[RequiresPermission(LocalizationPermissions.Manage)]
public sealed record TranslateMissingCommand(string Culture) : ICommand<Result<TranslateMissingDto>>;
