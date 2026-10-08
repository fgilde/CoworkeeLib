using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Localization;
using Coworkee.Core.Results;

namespace Coworkee.Localization.Features.Texts;

[RequiresPermission(LocalizationPermissions.Manage)]
public sealed record SetTranslationCommand(SetTranslationRequest Translation) : ICommand<Result>;
