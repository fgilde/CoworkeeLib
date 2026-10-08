using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Localization;
using Coworkee.Core.Results;

namespace Coworkee.Localization.Features.Languages;

[RequiresPermission(LocalizationPermissions.Manage)]
public sealed record AddEditLanguageCommand(Guid? Id, AddEditLanguageRequest Language) : ICommand<Result<LanguageDto>>;
