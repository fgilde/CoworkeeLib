using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Auditing;
using Coworkee.Core.Results;

namespace Coworkee.Auditing;

[RequiresPermission(AuditPermissions.View)]
public sealed record GetAuditEntries(AuditQuery Query) : IQuery<Result<PagedResult<AuditEntryDto>>>;
