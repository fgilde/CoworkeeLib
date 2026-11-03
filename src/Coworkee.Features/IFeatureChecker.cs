namespace Coworkee.Features;

public interface IFeatureChecker
{
    /// <summary>The value for the current tenant: its override, else its edition's value, else the default.</summary>
    Task<string?> GetValueAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> IsEnabledAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string?>> GetAllAsync(CancellationToken cancellationToken = default);
}
