using Coworkee.Domain;

namespace Coworkee.Mailing;

public sealed record MailQueued(Guid MailId) : IDomainEvent;
