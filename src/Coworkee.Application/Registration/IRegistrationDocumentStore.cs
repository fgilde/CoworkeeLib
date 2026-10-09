using Coworkee.Contracts.Configuration;

namespace Coworkee.Application.Registration;

/// <summary>A file a new user uploaded in the registration wizard for one of the configured slots.</summary>
public sealed record RegistrationDocument(Guid UserId, string UserName, RegistrationDocumentSlot Slot, string FileName, string ContentType, long Size, Stream Content);

/// <summary>
/// Keeps registration documents where the app wants them, private to the new user and administrators. It runs as the new user in
/// the unit of work that creates the account; the caller saves the changes. Coworkee.Files brings a default (a folder per user).
/// </summary>
public interface IRegistrationDocumentStore
{
    Task SaveAsync(RegistrationDocument document, CancellationToken cancellationToken);
}
