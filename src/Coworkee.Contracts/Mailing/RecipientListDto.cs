namespace Coworkee.Contracts.Mailing;

public sealed record RecipientListDto(Guid Id, string Name, RecipientSourceDto Source);
