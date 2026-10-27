using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Identity.ApiKeys;

// never AI tools: a created secret must not end up in a model's context, and a key must not mint keys
[AiTool(Exclude = true)]
[RequiresPermission(ApiKeyPermissions.Create)]
public sealed record GetMyApiKeys : IQuery<Result<IReadOnlyList<ApiKeyDto>>>;

[AiTool(Exclude = true)]
[RequiresPermission(ApiKeyPermissions.Create)]
public sealed record GetApiKeyScopes : IQuery<Result<IReadOnlyList<PermissionGroupDto>>>;

[AiTool(Exclude = true)]
[RequiresPermission(ApiKeyPermissions.Create)]
[NotWhileImpersonating]
public sealed record CreateApiKey(CreateApiKeyRequest Key) : ICommand<Result<CreatedApiKeyDto>>;

[AiTool(Exclude = true)]
[RequiresPermission(ApiKeyPermissions.Create)]
public sealed record RevokeApiKey(Guid Id) : ICommand<Result>;

internal sealed class CreateApiKeyValidator : AbstractValidator<CreateApiKey>
{
    public CreateApiKeyValidator()
    {
        RuleFor(c => c.Key.Name).NotEmpty().MaximumLength(ApiKeyLimits.Name);
        RuleFor(c => c.Key.Description).MaximumLength(ApiKeyLimits.Description);
        RuleFor(c => c.Key.Scopes).NotEmpty().Must(s => s.Count <= 500);
    }
}
