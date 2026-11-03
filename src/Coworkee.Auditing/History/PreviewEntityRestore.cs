using Coworkee.Application.Messaging;
using Coworkee.Contracts.Auditing;
using Coworkee.Core.Results;

namespace Coworkee.Auditing.History;

public sealed record PreviewEntityRestore(string EntitySet, Guid Id, EntityHistoryRestoreRequest Request) : IQuery<Result<EntityHistoryPreviewDto>>;
