namespace Coworkee.Core.Security;

public interface IUserDirectory
{
    Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken);
}
