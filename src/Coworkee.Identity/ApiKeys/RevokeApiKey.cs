using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.ApiKeys;

[AiTool(Exclude = true)]
[RequiresPermission(ApiKeyPermissions.Create)]
public sealed record RevokeApiKey(Guid Id) : ICommand<Result>;
