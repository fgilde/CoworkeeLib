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
                yield return T("\"{0}\" is larger than {1}.", slot.DisplayName, max >= 1024 * 1024 ? $"{max / 1024d / 1024d:0.#} MB" : $"{Math.Ceiling(max / 1024d):0} KB");
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
