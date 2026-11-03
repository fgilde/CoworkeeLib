namespace Coworkee.Contracts.Mailing;

public sealed record RecipientRowDto(Guid Id, string Email, string? DisplayName, DateTimeOffset? LastLoginAt);
