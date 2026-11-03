using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Features;
using Coworkee.Core.Results;

namespace Coworkee.Features;

[RequiresPermission(FeaturePermissions.Editions)]
public sealed record UpdateEdition(Guid Id, EditionRequest Edition) : ICommand<Result<EditionDto>>;
