using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Features;
using Coworkee.Core.Results;

namespace Coworkee.Features;

[Coworkee.Application.Messaging.AiTool(Exclude = true)]
[RequiresPermission(FeaturePermissions.Tenants)]
public sealed record CreateTenant(CreateTenantRequest Tenant) : ICommand<Result<Guid>>;
