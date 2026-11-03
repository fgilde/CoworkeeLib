namespace Coworkee.Application.Privacy;

/// <summary>A module's share of a user's personal data (GDPR): exported as one named JSON section, erased when the account is deleted.</summary>
public interface IPersonalDataContributor
{
    string Section { get; }

    /// <summary>Anything System.Text.Json can serialize; null leaves the section out.</summary>
    Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes or anonymizes the data. Runs inside the erase transaction; changes may be tracked or executed directly. The user's id in
    /// CreatedBy, ModifiedBy and DeletedBy columns, the audit trail, the snapshots and the outbox is replaced afterwards by the library.
    /// </summary>
    Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken);
}
