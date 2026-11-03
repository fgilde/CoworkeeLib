using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.ApiKeys;

// never AI tools: a created secret must not end up in a model's context, and a key must not mint keys
[AiTool(Exclude = true)]
[RequiresPermission(ApiKeyPermissions.Create)]
public sealed record GetMyApiKeys : IQuery<Result<IReadOnlyList<ApiKeyDto>>>;
