namespace Coworkee.Contracts.Settings;

/// <summary>Something that went wrong recently (a failed job, a mail that could not be sent), shown as it is.</summary>
public sealed record SystemStatusProblem(string Title, string Message, DateTimeOffset? At = null);
