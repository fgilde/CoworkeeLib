namespace Coworkee.Contracts.Identity;

public sealed record SetupCheckDto(string Name, SetupCheckStatus Status, string? Message);
