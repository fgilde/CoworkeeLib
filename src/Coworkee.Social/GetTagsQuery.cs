using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Tags.View)]
[Description("Lists the tag set of an entity type with how many entities carry each tag, most used first.")]
public sealed record GetTagsQuery(string EntityType) : IQuery<Result<IReadOnlyList<TagDto>>>;
