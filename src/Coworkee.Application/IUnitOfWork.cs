namespace Coworkee.Application;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>The app's DbContext has an open transaction: what it wrote is not committed yet and may be rolled back.</summary>
    bool InTransaction => false;
}
