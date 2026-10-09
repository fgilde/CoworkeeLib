namespace Coworkee.Application.Privacy;

/// <summary>The user whose data is exported or erased; read before any contributor runs, so the order of the contributors does not matter.</summary>
public sealed record PersonalDataSubject(Guid UserId, Guid TenantId, string? Email);

/// <summary>A module's share of a user's personal data (GDPR): exported as one named JSON section, erased when the account is deleted.</summary>
public interface IPersonalDataContributor
{
    string Section { get; }

    /// <summary>Anything System.Text.Json can serialize; null leaves the section out.</summary>
    Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken);

    /// <summary>Deletes or anonymizes the data. Runs inside the erase transaction; changes may be tracked or executed directly.</summary>
    Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken);
}
