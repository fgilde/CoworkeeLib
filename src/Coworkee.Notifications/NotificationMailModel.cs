using System.Text.Json;
using Coworkee.Mailing;
using Microsoft.EntityFrameworkCore;
using Scriban.Runtime;

namespace Coworkee.Notifications;

/// <summary>The model of a notification mail: the members of <see cref="NotificationMessage.MailModel"/> next to <c>user</c> and <c>notification</c>.</summary>
public static class NotificationMailModel
{
    /// <summary>The largest model, as UTF-8 JSON.</summary>
    public const int MaxBytes = 32 * 1024;

    // names the library fills (user, notification, the layout's subject, content and unsubscribe_url) and Scriban's builtin objects; compared without case and underscores
    private static readonly HashSet<string> Reserved = new(
        new[] { "user", "notification", "subject", "content", "unsubscribe_url" }.Concat(new Scriban.Functions.BuiltinFunctions().Keys).Select(Normalize),
        StringComparer.Ordinal);
    private static readonly object SampleUser = new { first_name = "Ada", email = "ada@example.com" };

    private static readonly object SampleNotification = new { title = "Document shared", body = "Grace shared a document with you.", link = "https://example.com/d/1" };

    /// <summary>
    /// The sample of a notification type's mail template (<see cref="MailTemplateDefinition.SampleModel"/>): <paramref name="model"/> with a sample
    /// <c>user</c> and <c>notification</c>, so the preview and the test mail on the mail templates page show what a real mail shows.
    /// </summary>
    public static object Sample(object model)
    {
        var sample = new ScriptObject();
        foreach (var property in model.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0))
        {
            sample[StandardMemberRenamer.Default(property)] = property.GetValue(model);
        }

        sample["user"] = SampleUser;
        sample["notification"] = SampleNotification;
        return sample;
    }

    /// <summary>The model as JSON for the waiting mail: an object (or a dictionary) of at most <see cref="MaxBytes"/> without reserved top-level names.</summary>
    internal static string? Json(object? model)
    {
        if (model is null)
        {
            return null;
        }

        var json = JsonSerializer.SerializeToElement(model);
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The mail model of a notification is an object or a dictionary.", nameof(model));
        }

        if (json.EnumerateObject().Select(p => p.Name).FirstOrDefault(IsReserved) is { } reserved)
        {
            throw new ArgumentException($"The mail model of a notification cannot have a member '{reserved}': the library fills it.", nameof(model));
        }

        var text = json.GetRawText();
        return System.Text.Encoding.UTF8.GetByteCount(text) <= MaxBytes
            ? text
            : throw new ArgumentException($"The mail model of a notification is at most {MaxBytes / 1024} KB as JSON.", nameof(model));
    }

    /// <summary>The model's members next to the trusted <c>user</c> and <c>notification</c>, which always win; reserved names of older rows are left out.</summary>
    internal static Dictionary<string, object?> Build(string? json, object user, object notification)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (json is not null)
        {
            foreach (var (name, value) in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!)
            {
                if (!IsReserved(name))
                {
                    values[StandardMemberRenamer.Rename(name)] = value;
                }
            }
        }

        values["user"] = user;
        values["notification"] = notification;
        return values;
    }

    private static bool IsReserved(string name) => Reserved.Contains(Normalize(name));

    private static string Normalize(string name) => name.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}
