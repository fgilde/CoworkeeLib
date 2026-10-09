using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Tags.View)]
[Description("Lists the tag set of an entity type with how many entities carry each tag, most used first.")]
public sealed record GetTagsQuery(string EntityType) : IQuery<Result<IReadOnlyList<TagDto>>>;

[RequiresPermission(SocialPermissions.Tags.View)]
[Description("Reads the tags of one entity.")]
public sealed record GetEntityTagsQuery(string EntityType, Guid EntityId) : IQuery<Result<EntityTagsDto>>;

[RequiresPermission(SocialPermissions.Tags.View)]
[Description("Lists the ids of the entities of a type that carry a tag.")]
public sealed record GetTaggedEntitiesQuery(string EntityType, string Tag) : IQuery<Result<IReadOnlyList<Guid>>>;

[RequiresPermission(SocialPermissions.Tags.Create)]
[Description("Replaces the tags of one entity; unknown tags join the tag set of the entity type.")]
public sealed record SetEntityTagsCommand(string EntityType, Guid EntityId, IReadOnlyList<string> Tags) : ICommand<Result<EntityTagsDto>>;

[RequiresPermission(SocialPermissions.Tags.Moderate)]
[Description("Removes a tag from the tag set of an entity type and from every entity.")]
public sealed record DeleteTagCommand(string EntityType, string Tag) : ICommand<Result>;

internal sealed class SetEntityTagsValidator : AbstractValidator<SetEntityTagsCommand>
{
    public SetEntityTagsValidator()
    {
        RuleFor(c => c.Tags).Must(t => t.Count <= 50).WithMessage("An entity carries at most 50 tags.");
        RuleForEach(c => c.Tags).NotEmpty().Must(t => t.Trim().Length <= SocialLimits.TagName).WithMessage($"A tag has at most {SocialLimits.TagName} characters.");
    }
}
