using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.ApiKeys;

[AiTool(Exclude = true)]
[RequiresPermission(ApiKeyPermissions.Create)]
[NotWhileImpersonating]
public sealed record CreateApiKey(CreateApiKeyRequest Key) : ICommand<Result<CreatedApiKeyDto>>;
