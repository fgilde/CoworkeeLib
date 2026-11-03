namespace Coworkee.Contracts.Mailing;

public sealed record SaveRecipientListRequest(string Name, RecipientSourceDto Source);
