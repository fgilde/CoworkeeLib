using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Localization;
using Coworkee.Core.Results;

namespace Coworkee.Localization.Features.Texts;

/// <summary>Every known key with the module default and the edit of the culture, for the translation editor.</summary>
[RequiresPermission(LocalizationPermissions.Manage)]
public sealed record GetTranslationRowsQuery(string Culture) : IQuery<Result<IReadOnlyList<TranslationRowDto>>>;
