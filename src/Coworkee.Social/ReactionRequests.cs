using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Reactions.View)]
[Description("Reads the emoji reactions of an entity with who reacted.")]
public sealed record GetReactionsQuery(string EntityType, Guid EntityId) : IQuery<Result<ReactionsDto>>;

[RequiresPermission(SocialPermissions.Reactions.Create)]
[Description("Adds an emoji reaction to an entity, or takes it back when the signed-in user already reacted with it.")]
public sealed record ToggleReactionCommand(string EntityType, Guid EntityId, string Emoji) : ICommand<Result<ReactionsDto>>;

internal sealed class ToggleReactionValidator : AbstractValidator<ToggleReactionCommand>
{
    public ToggleReactionValidator() =>
        RuleFor(c => c.Emoji).NotEmpty().MaximumLength(SocialLimits.Emoji).Must(e => e.All(c => c >= 128)).WithMessage("Use an emoji.");
}
