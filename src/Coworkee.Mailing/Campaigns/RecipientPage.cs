namespace Coworkee.Mailing;

public sealed record RecipientPage(IReadOnlyList<MailRecipient> Recipients, Guid? Next);
