using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

/// <summary>Either <see cref="Content"/> to save or the <see cref="Job"/> that writes the file.</summary>
public sealed record ExportResult(byte[]? Content, string FileName, string ContentType, DataTransferJobDto? Job);
