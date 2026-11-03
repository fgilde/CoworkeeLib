using Coworkee.Application.Messaging;
using Coworkee.Contracts.Features;
using Coworkee.Core.Results;

namespace Coworkee.Features;

public sealed record GetFeatureDefinitions : IQuery<Result<IReadOnlyList<FeatureGroupDto>>>;
