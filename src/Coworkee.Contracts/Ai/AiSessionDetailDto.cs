namespace Coworkee.Contracts.Ai;

public sealed record AiSessionDetailDto(Guid Id, string Title, DateTimeOffset UpdatedAt, IReadOnlyList<AiMessageDto> Messages);
