namespace Coworkee.Contracts.Mailing;

/// <summary>A saved state of a template language, newest first in the history; <paramref name="Author"/> is the display name of who saved it.</summary>
public sealed record MailTemplateVersionDto(DateTimeOffset SavedAt, string? Author, string Subject, string Body);
