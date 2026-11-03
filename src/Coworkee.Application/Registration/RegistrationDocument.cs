using Coworkee.Contracts.Configuration;

namespace Coworkee.Application.Registration;

/// <summary>A file a new user uploaded in the registration wizard for one of the configured slots.</summary>
public sealed record RegistrationDocument(Guid UserId, string UserName, RegistrationDocumentSlot Slot, string FileName, string ContentType, long Size, Stream Content);
