using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Distributed;

namespace Coworkee.Bff;

/// <summary>Keeps the sign-in with its tokens in the distributed cache, so the browser cookie holds only a session key and stays small for every localhost port.</summary>
public sealed class CacheTicketStore(IDistributedCache cache) : ITicketStore
{
    private const string Prefix = "coworkee:bff:session:";

    private static readonly TimeSpan Fallback = TimeSpan.FromDays(14);

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString("N");
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        cache.SetAsync(Prefix + key, TicketSerializer.Default.Serialize(ticket), new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.Add(Fallback),
        });

    public async Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        await cache.GetAsync(Prefix + key) is { } bytes ? TicketSerializer.Default.Deserialize(bytes) : null;

    public Task RemoveAsync(string key) => cache.RemoveAsync(Prefix + key);
}
