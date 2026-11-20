namespace Coworkee.Contracts.Social;

/// <summary>The blocks the server opted an entity type into with <c>AddCoworkeeSocial</c>; each still needs its view permission.</summary>
public sealed record SocialFeaturesDto(bool Comments, bool Tags, bool Ratings, bool Reactions);
