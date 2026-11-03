using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Webhooks;
using Coworkee.Core.Results;

namespace Coworkee.Webhooks;

[RequiresPermission(WebhookPermissions.Manage)]
[AiTool(Exclude = true)]
public sealed record GetWebhookSecretQuery(Guid Id) : IQuery<Result<WebhookSecretDto>>;
