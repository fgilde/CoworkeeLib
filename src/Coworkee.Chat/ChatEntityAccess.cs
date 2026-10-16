using Coworkee.Application.GlobalSearch;
using Coworkee.Contracts.Chat;

namespace Coworkee.Chat;

/// <summary>
/// Whether the current user may see a mentioned record: its global search provider must find it for him, by id (a GUID) or by its exact label.
/// One answer per record and request.
/// </summary>
internal sealed class ChatEntityAccess(GlobalSearcher searcher)
{
    private const int Candidates = 5;
    private readonly Dictionary<(string Provider, string Id), bool> _seen = [];

    public async Task<Func<ChatMentionItem, bool>> CheckAsync(IEnumerable<ChatMentionItem> mentions, CancellationToken cancellationToken)
    {
        foreach (var mention in mentions.Where(m => m.Kind == ChatMentionItem.Entity))
        {
            var key = (mention.Provider!, mention.Id);
            if (!_seen.ContainsKey(key))
            {
                _seen[key] = await FindAsync(mention, cancellationToken);
            }
        }

        return mention => _seen.GetValueOrDefault((mention.Provider ?? string.Empty, mention.Id));
    }

    // ponytail: one provider search per distinct record and request; a batch lookup by ids if chats mention many records
    private async Task<bool> FindAsync(ChatMentionItem mention, CancellationToken cancellationToken)
    {
        var text = Guid.TryParse(mention.Id, out _) ? mention.Id : $"\"{mention.Label}\"";
        var result = await searcher.SearchAsync(text, [mention.Provider!], Candidates, cancellationToken);
        return result.Hits.Any(h => h.Provider == mention.Provider && string.Equals(h.Id, mention.Id, StringComparison.OrdinalIgnoreCase));
    }
}
