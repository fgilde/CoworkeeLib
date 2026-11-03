namespace Coworkee.OData.Transfer;

public sealed record ExportFile(byte[] Content, string ContentType, string Name);
