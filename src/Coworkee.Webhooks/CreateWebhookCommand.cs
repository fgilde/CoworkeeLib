using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Webhooks;
using Coworkee.Core.Results;

namespace Coworkee.Webhooks;

[RequiresPermission(WebhookPermissions.Manage)]
[AiTool(Exclude = true)]
[NotWhileImpersonating]
public sealed record CreateWebhookCommand(WebhookRequest Request) : ICommand<Result<WebhookSecretDto>>;
