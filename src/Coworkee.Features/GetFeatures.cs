using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Features;

public sealed record GetFeatures : IQuery<Result<IReadOnlyDictionary<string, string?>>>;
