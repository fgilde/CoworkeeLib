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

public sealed record MailTemplateSummaryDto(
    string Name, string DisplayName, IReadOnlyList<string> Cultures, IReadOnlyList<string> OverriddenCultures, bool IsCustom, string? Description);

public sealed record CreateMailTemplateRequest(string Name, string DisplayName, string? Description, bool Global = false, string? CopyFrom = null);

public sealed record MailVariableDto(string Name, string Type, string? Sample, bool IsList);

public sealed record MailTemplateDto(
    string Name, string Culture, string Subject, string Body, string DefaultSubject, string DefaultBody, bool IsOverridden, IReadOnlyList<MailVariableDto> Variables);

public sealed record SaveMailTemplateRequest(string Subject, string Body);

/// <summary>The mail settings as typed in the form; an empty secret stands for the stored one unless its setting name is in <paramref name="Cleared"/> (the form removes it).</summary>
public sealed record MailConnectionTestRequest(IReadOnlyDictionary<string, string?> Values, IReadOnlyCollection<string>? Cleared = null);

public sealed record MailConnectionTestDto(bool Success, string? Error);

public sealed record RenderedMailDto(string Subject, string HtmlBody);

public sealed record OutgoingMailDto(
    Guid Id, string To, string Subject, string TemplateName, OutgoingMailStatus Status, int Attempts, string? LastError, DateTimeOffset QueuedAt, DateTimeOffset? SentAt);
