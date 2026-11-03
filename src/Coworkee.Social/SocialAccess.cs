namespace Coworkee.Social;

/// <summary>What is about to happen: <see cref="EntityId"/> is null for questions about the whole entity type, such as the tag cloud.</summary>
public sealed record SocialAccess(SocialFeature Feature, string EntityType, Guid? EntityId, bool Write);
