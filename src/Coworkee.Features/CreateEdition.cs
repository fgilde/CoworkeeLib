using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Features;
using Coworkee.Core.Results;

namespace Coworkee.Features;

[RequiresPermission(FeaturePermissions.Editions)]
public sealed record CreateEdition(EditionRequest Edition) : ICommand<Result<EditionDto>>;
