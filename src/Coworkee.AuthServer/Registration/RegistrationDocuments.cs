using Coworkee.Application.Registration;
using Coworkee.Contracts.Configuration;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Http;
using static Coworkee.AuthServer.AuthTexts;

namespace Coworkee.AuthServer.Registration;

/// <summary>The upload fields of the document step, one per slot, named <see cref="Field"/>.</summary>
public static class RegistrationDocuments
{
    public static string Field(int slot) => $"Document{slot}";

    /// <summary>The accept attribute of the file field; null when a pattern has a wildcard browsers do not understand.</summary>
    public static string? Accept(RegistrationDocumentSlot slot) =>
        slot.ContentTypes.Count > 0 && slot.ContentTypes.All(t => !t.Contains('*', StringComparison.Ordinal) || t.EndsWith("/*", StringComparison.Ordinal))
            ? string.Join(',', slot.ContentTypes)
            : null;

    /// <summary>The slot's rule in words for the upload field: "PDF, Images · up to 10 MB".</summary>
    public static string Rule(RegistrationDocumentSlot slot) =>
        (slot.ContentTypes.Count == 0 ? T("Every file type") : string.Join(", ", slot.ContentTypes.Select(TypeName).Distinct(StringComparer.Ordinal)))
        + (slot.MaxSize is > 0 and var max ? " · " + T("up to {0}", Size(max)) : string.Empty);

    /// <summary>"1.5 MB" or "800 KB" in the current culture; the script of the documents step formats the same way.</summary>
    public static string Size(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.#} MB" : $"{Math.Ceiling(bytes / 1024d):0} KB";

    // a group word for wildcards and office families, the short format name ("PNG", "JSON") for single types, else the pattern itself
    private static string TypeName(string pattern)
    {
        var type = pattern.Trim().ToLowerInvariant();
        var (major, minor) = type.IndexOf('/') is > 0 and var slash ? (type[..slash], type[(slash + 1)..]) : (type, string.Empty);
        return type switch
        {
            "application/pdf" => "PDF",
            "text/plain" => T("Text files"),
            _ when minor == "*" => major switch { "image" => T("Images"), "video" => T("Videos"), "audio" => T("Audio"), "text" => T("Text files"), _ => pattern },
            _ when type.Contains("spreadsheet", StringComparison.Ordinal) || type.Contains("excel", StringComparison.Ordinal) || type == "text/csv" => T("Spreadsheets"),
            _ when type.Contains("presentation", StringComparison.Ordinal) || type.Contains("powerpoint", StringComparison.Ordinal) => T("Presentations"),
            _ when type.Contains("word", StringComparison.Ordinal) || type.Contains("opendocument.text", StringComparison.Ordinal) || type == "application/rtf" => T("Text documents"),
            _ when type.Contains("officedocument", StringComparison.Ordinal) || type.Contains("opendocument", StringComparison.Ordinal) => T("Office documents"),
            _ when type.Contains("zip", StringComparison.Ordinal) || type.Contains("compressed", StringComparison.Ordinal) || type.Contains("x-tar", StringComparison.Ordinal) => T("Archives"),
            _ when major is "image" or "video" or "audio" or "text" or "application" && !minor.Contains('*', StringComparison.Ordinal)
                => minor.Split('+')[0].Replace("x-", string.Empty, StringComparison.Ordinal).Replace("vnd.", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
            _ => pattern,
        };
    }

    public static IEnumerable<string> Validate(IReadOnlyList<RegistrationDocumentSlot> slots, IFormFileCollection files)
    {
        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            if (files.GetFile(Field(index)) is not { Length: > 0 } file)
            {
                if (slot.Required)
                {
                    yield return T("Add the document \"{0}\".", slot.DisplayName);
                }

                continue;
            }

            if (!Wildcards.Allows(slot.ContentTypes, file.ContentType))
            {
                yield return T("\"{0}\" has a file type that is not accepted.", slot.DisplayName);
            }

            if (slot.MaxSize is > 0 and var max && file.Length > max)
            {
                yield return T("\"{0}\" is larger than {1}.", slot.DisplayName, Size(max));
            }
        }
    }

    public static async Task SaveAsync(
        IRegistrationDocumentStore store, User user, IReadOnlyList<RegistrationDocumentSlot> slots, IFormFileCollection files, CancellationToken cancellationToken)
    {
        for (var index = 0; index < slots.Count; index++)
        {
            if (files.GetFile(Field(index)) is { Length: > 0 } file)
            {
                await using var content = file.OpenReadStream();
                await store.SaveAsync(
                    new RegistrationDocument(user.Id, user.Email!, slots[index], Path.GetFileName(file.FileName), file.ContentType, file.Length, content), cancellationToken);
            }
        }
    }
}
