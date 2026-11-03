namespace Coworkee.Contracts.Mailing;

public sealed record CreateMailTemplateRequest(string Name, string DisplayName, string? Description, bool Global = false, string? CopyFrom = null);
