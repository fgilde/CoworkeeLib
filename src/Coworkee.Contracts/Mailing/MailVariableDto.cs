namespace Coworkee.Contracts.Mailing;

public sealed record MailVariableDto(string Name, string Type, string? Sample, bool IsList);
