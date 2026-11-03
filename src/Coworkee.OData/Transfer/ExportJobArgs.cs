namespace Coworkee.OData.Transfer;

/// <param name="Groups">The groups selected when the export was requested; the job lists the rows of them, as the user's list did.</param>
/// <param name="Mail">Mails the file to the owner and <paramref name="Recipients"/> when it is written.</param>
public sealed record ExportJobArgs(Guid JobId, Guid[]? Groups = null, bool Mail = false, string[]? Recipients = null);
