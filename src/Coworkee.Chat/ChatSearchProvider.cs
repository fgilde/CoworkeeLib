using System.Runtime.CompilerServices;
using Coworkee.Application.GlobalSearch;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Chat;

/// <summary>The user's own chats whose messages contain every word, newest message per chat.</summary>
internal sealed class ChatSearchProvider : IGlobalSearchProvider
{
    private const int SnippetLength = 120;

    public string Name => "chats";

    public string Group => "Chat";

    public string? Icon => "Chat";

    public string? Permission => ChatPermissions.Use;

    public async IAsyncEnumerable<GlobalSearchHit> SearchAsync(GlobalSearchQuery query, GlobalSearchContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var views = context.Services.GetRequiredService<ChatViews>();
        var db = context.Services.GetRequiredService<Infrastructure.Persistence.CoworkeeDbContext>();
        if (context.Services.GetRequiredService<Core.Security.ICurrentUser>().UserId is null)
        {
            yield break;
        }

        var mine = views.Memberships().Select(m => m.ConversationId);
        var messages = query.Words.Aggregate(
            db.Set<ChatMessage>().Where(m => mine.Contains(m.ConversationId) && m.DeletedAt == null),
            (rows, word) => rows.Where(m => m.Text.ToLower().Contains(word.ToLower())));
        // ponytail: newest matching messages, one per chat in memory; a GROUP BY if chats get long
        var found = await messages.OrderByDescending(m => m.SentAt).Take(context.Limit * 10).Select(m => new { m.ConversationId, m.Text, m.SentAt }).ToListAsync(cancellationToken);
        var latest = found.DistinctBy(m => m.ConversationId).Take(context.Limit).ToList();
        var ids = latest.Select(m => m.ConversationId).ToList();
        var chats = (await views.ConversationsAsync(db.Set<ChatConversation>().Where(c => ids.Contains(c.Id)), cancellationToken))
            .ToDictionary(c => c.Id);
        foreach (var message in latest.Where(m => chats.ContainsKey(m.ConversationId)))
        {
            var chat = chats[message.ConversationId];
            yield return new GlobalSearchHit(Name, Group, chat.Name)
            {
                Subtitle = message.Text.Length > SnippetLength ? message.Text[..SnippetLength] + "…" : message.Text,
                Url = $"/chat?c={chat.Id}",
                Id = chat.Id.ToString(),
                Icon = chat.IsGroup ? "Forum" : null,
                Score = (SearchMatch.Score(message.Text, query)?.Score ?? 0.3) * 0.9,
            };
        }
    }
}
