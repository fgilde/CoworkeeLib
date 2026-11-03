using Coworkee.Application.Messaging;
using Coworkee.Contracts.Auditing;
using Coworkee.Core.Results;

namespace Coworkee.Auditing;

public sealed record GetEntityVersion(string Type, Guid Id, int Revision) : IQuery<Result<EntityVersionDetailDto>>;
