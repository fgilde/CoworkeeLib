namespace Coworkee.Contracts.Mailing;

public sealed record RecipientPreviewDto(int Count, int Excluded, IReadOnlyList<RecipientRowDto> First);
