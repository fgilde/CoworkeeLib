using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Features;
using Coworkee.Core.Results;

namespace Coworkee.Features;

[RequiresPermission(FeaturePermissions.Tenants)]
public sealed record GetTenantDetails(IReadOnlyList<Guid> Ids) : IQuery<Result<IReadOnlyList<TenantDetailsDto>>>;
