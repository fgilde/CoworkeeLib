using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Mailing;

public static class MailPermissions
{
    public const string GroupName = "Mail";

    public static class Templates
    {
        public const string Manage = "Mail.Templates.Manage";
    }

    public static class Log
    {
        public const string View = "Mail.Log.View";
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<OutgoingMailStatus>))]
public enum OutgoingMailStatus
{
    Queued,
    Sending,
    Sent,
    Failed,
    Skipped,
}

public sealed record MailTemplateSummaryDto(string Name, string DisplayName, IReadOnlyList<string> Cultures, IReadOnlyList<string> OverriddenCultures);

public sealed record MailTemplateDto(string Name, string Culture, string Subject, string Body, string DefaultSubject, string DefaultBody, bool IsOverridden);

public sealed record SaveMailTemplateRequest(string Subject, string Body);

public sealed record RenderedMailDto(string Subject, string HtmlBody);

public sealed record OutgoingMailDto(
    Guid Id, string To, string Subject, string TemplateName, OutgoingMailStatus Status, int Attempts, string? LastError, DateTimeOffset QueuedAt, DateTimeOffset? SentAt);
