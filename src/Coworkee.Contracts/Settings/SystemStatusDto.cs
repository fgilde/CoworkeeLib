namespace Coworkee.Contracts.Settings;

public sealed record SystemStatusDto(DateTimeOffset At, IReadOnlyList<SystemStatusSection> Sections);
