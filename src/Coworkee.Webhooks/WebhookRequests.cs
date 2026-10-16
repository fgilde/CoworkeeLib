using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Webhooks;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Webhooks;

[RequiresPermission(WebhookPermissions.Manage)]
[AiTool(Exclude = true)]
public sealed record GetWebhooksQuery : IQuery<Result<IReadOnlyList<WebhookSubscriptionDto>>>;

[RequiresPermission(WebhookPermissions.Manage)]
[AiTool(Exclude = true)]
public sealed record GetWebhookEventTypesQuery : IQuery<Result<IReadOnlyList<WebhookEventTypeDto>>>;

[RequiresPermission(WebhookPermissions.Manage)]
[AiTool(Exclude = true)]
public sealed record CreateWebhookCommand(WebhookRequest Request) : ICommand<Result<WebhookSecretDto>>;

[RequiresPermission(WebhookPermissions.Manage)]
[AiTool(Exclude = true)]
public sealed record UpdateWebhookCommand(Guid Id, WebhookRequest Request) : ICommand<Result>;

[RequiresPermission(WebhookPermissions.Manage)]
[AiTool(Exclude = true)]
public sealed record DeleteWebhookCommand(Guid Id) : ICommand<Result>;

[RequiresPermission(WebhookPermissions.Manage)]
[AiTool(Exclude = true)]
public sealed record GetWebhookSecretQuery(Guid Id) : IQuery<Result<WebhookSecretDto>>;

[RequiresPermission(WebhookPermissions.Manage)]
[AiTool(Exclude = true)]
public sealed record RotateWebhookSecretCommand(Guid Id) : ICommand<Result<WebhookSecretDto>>;

[RequiresPermission(WebhookPermissions.Manage)]
[AiTool(Exclude = true)]
public sealed record SendWebhookTestCommand(Guid Id) : ICommand<Result<Guid>>;

[RequiresPermission(WebhookPermissions.Manage)]
[AiTool(Exclude = true)]
public sealed record GetWebhookDeliveriesQuery(Guid? SubscriptionId = null, int Take = 50) : IQuery<Result<IReadOnlyList<WebhookDeliveryDto>>>;

[RequiresPermission(WebhookPermissions.Manage)]
[AiTool(Exclude = true)]
public sealed record RedeliverWebhookCommand(Guid DeliveryId) : ICommand<Result<Guid>>;

internal sealed class WebhookRequestValidator : AbstractValidator<WebhookRequest>
{
    public WebhookRequestValidator(WebhookUrlGuard guard)
    {
        RuleFor(r => r.Url).NotEmpty().MaximumLength(WebhookLimits.Url).Custom((url, context) =>
        {
            if (!string.IsNullOrEmpty(url) && guard.Check(url) is { } problem)
            {
                context.AddFailure(problem);
            }
        });
        RuleFor(r => r.Events).NotEmpty();
        RuleForEach(r => r.Events).NotEmpty().MaximumLength(WebhookLimits.EventType);
        RuleFor(r => r.Description).MaximumLength(500);
        RuleFor(r => r.Secret).Must(s => s is null || WebhookSignature.IsValidSecret(s)).WithMessage("Use a whsec_ secret or at least 16 characters.");
    }
}

internal sealed class CreateWebhookValidator : AbstractValidator<CreateWebhookCommand>
{
    public CreateWebhookValidator(WebhookUrlGuard guard) => RuleFor(c => c.Request).SetValidator(new WebhookRequestValidator(guard));
}

internal sealed class UpdateWebhookValidator : AbstractValidator<UpdateWebhookCommand>
{
    public UpdateWebhookValidator(WebhookUrlGuard guard) => RuleFor(c => c.Request).SetValidator(new WebhookRequestValidator(guard));
}

internal sealed class GetWebhookDeliveriesValidator : AbstractValidator<GetWebhookDeliveriesQuery>
{
    public GetWebhookDeliveriesValidator() => RuleFor(q => q.Take).InclusiveBetween(1, 200);
}
