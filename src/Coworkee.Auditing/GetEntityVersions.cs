using Coworkee.Application.Messaging;
using Coworkee.Contracts.Auditing;
using Coworkee.Core.Results;

namespace Coworkee.Auditing;

public sealed record GetEntityVersions(string Type, Guid Id) : IQuery<Result<IReadOnlyList<EntityVersionDto>>>;
