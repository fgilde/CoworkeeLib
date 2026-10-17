using Coworkee.Client.Blazor.Search;
using Coworkee.Contracts.Chat;

namespace Coworkee.Client.Blazor.Chat;

/// <summary>Suggestions of the chat editor: <c>@</c> people of the organisation, <c>#</c> records of the global search the user may see.</summary>
public sealed class ChatMentionSource(IChatApi chat, IGlobalSearchApi? search)
{
    public const int MaxSuggestions = 8;
    private const int MinRecordQuery = 2;
    private static readonly TimeSpan RecordTimeout = TimeSpan.FromSeconds(4);
    private IReadOnlyList<ChatContactDto>? _people;

    public async Task<IEnumerable<ChatMentionItem>> SearchAsync(char denotation, string term)
    {
        term = term.Trim();
        return denotation == '@' ? await PeopleAsync(term) : await RecordsAsync(term);
    }

    private async Task<IEnumerable<ChatMentionItem>> PeopleAsync(string term)
    {
        _people ??= await chat.GetContactsAsync();
        return _people.Where(p => term.Length == 0 || p.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(p => !p.Name.StartsWith(term, StringComparison.CurrentCultureIgnoreCase)).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxSuggestions)
            .Select(p => new ChatMentionItem(ChatMentionItem.User, p.UserId.ToString(), p.Name));
    }

    private async Task<IEnumerable<ChatMentionItem>> RecordsAsync(string term)
    {
        if (search is null || term.Length < MinRecordQuery)
        {
            return [];
        }

        using var timeout = new CancellationTokenSource(RecordTimeout);
        var found = new List<ChatMentionItem>();
        try
        {
            await foreach (var item in search.StreamAsync(term, null, 3, timeout.Token))
            {
                if (item.Hit is { Id: { Length: > 0 } id, Provider: not "chats" } hit)
                {
                    found.Add(new ChatMentionItem(ChatMentionItem.Entity, id, hit.Title, hit.Provider, hit.Url, hit.Icon));
                }

                if (found.Count == MaxSuggestions)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        return found;
    }
}
