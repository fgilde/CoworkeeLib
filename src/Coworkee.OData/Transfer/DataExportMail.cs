namespace Coworkee.OData.Transfer;

/// <summary>An export file for its owner and further <paramref name="Recipients"/>.</summary>
public sealed record DataExportMail(Guid OwnerId, IReadOnlyList<string> Recipients, string Name, ExportFile File);

/// <summary>
/// Mails export files (the Mailing module implements it; OData cannot reference Mailing): exports whose client left before the file was ready,
/// queued exports and scheduled ones. The mails go into the current unit of work. Without an implementation nothing is mailed; the job keeps the file.
/// </summary>
public interface IExportMailer
{
    Task MailAsync(DataExportMail mail, CancellationToken cancellationToken);
}
