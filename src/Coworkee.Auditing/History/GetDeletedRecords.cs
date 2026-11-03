using Coworkee.Application.Messaging;
using Coworkee.Contracts.Auditing;
using Coworkee.Core.Results;

namespace Coworkee.Auditing.History;

public sealed record GetDeletedRecords(string EntitySet) : IQuery<Result<IReadOnlyList<DeletedRecordDto>>>;
