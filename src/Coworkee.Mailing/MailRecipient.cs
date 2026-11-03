namespace Coworkee.Mailing;

public sealed record MailRecipient(Guid Id, string Email, string? FirstName, string? LastName, string? Culture);
