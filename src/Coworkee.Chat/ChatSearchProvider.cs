using System.Runtime.CompilerServices;
using Coworkee.Application.GlobalSearch;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Search;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Chat;

/// <summary>The user's own conversations whose messages contain every word, newest message per person.</summary>
internal sealed class ChatSearchProvider : IGlobalSearchProvider
{
    private const int SnippetLength = 120;

    public string Name => "chats";

    public string Group => "Chats";

    public string? Icon => "Chat";

    public string? Permission => ChatPermissions.Use;

    public async IAsyncEnumerable<GlobalSearchHit> SearchAsync(GlobalSearchQuery query, GlobalSearchContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var db = context.Services.GetRequiredService<CoworkeeDbContext>();
        var currentUser = context.Services.GetRequiredService<ICurrentUser>();
        if (currentUser.UserId is not { } me)
        {
            yield break;
        }

        var messages = query.Words.Aggregate(
            db.Set<ChatMessage>().AsNoTracking().Where(m => m.FromUserId == me || m.ToUserId == me),
            (rows, word) => rows.Where(m => m.Text.ToLower().Contains(word.ToLower())));
        // ponytail: newest matching messages, one per person in memory; a GROUP BY if conversations get long
        var found = await messages.OrderByDescending(m => m.SentAt).Take(context.Limit * 10)
            .Select(m => new { Partner = m.FromUserId == me ? m.ToUserId : m.FromUserId, m.Text, m.SentAt }).ToListAsync(cancellationToken);
        var latest = found.DistinctBy(m => m.Partner).Take(context.Limit).ToList();
        var partners = latest.Select(m => m.Partner).ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => partners.Contains(u.Id) && u.TenantId == currentUser.TenantId)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email }).ToDictionaryAsync(u => u.Id, cancellationToken);
        foreach (var message in latest.Where(m => names.ContainsKey(m.Partner)))
        {
            var person = names[message.Partner];
            var name = string.Join(' ', new[] { person.FirstName, person.LastName }.Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } full ? full : person.Email ?? string.Empty;
            yield return new GlobalSearchHit(Name, Group, name)
            {
                Subtitle = message.Text.Length > SnippetLength ? message.Text[..SnippetLength] + "…" : message.Text,
                Url = $"/chat?with={message.Partner}",
                Id = message.Partner.ToString(),
                Score = (SearchMatch.Score(message.Text, query)?.Score ?? 0.3) * 0.9,
            };
        }
    }
}
