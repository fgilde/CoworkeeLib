namespace Coworkee.Application.Registration;

/// <summary>
/// Keeps registration documents where the app wants them, private to the new user and administrators. It runs as the new user in
/// the unit of work that creates the account; the caller saves the changes. Coworkee.Files brings a default (a folder per user).
/// </summary>
public interface IRegistrationDocumentStore
{
    Task SaveAsync(RegistrationDocument document, CancellationToken cancellationToken);
}
