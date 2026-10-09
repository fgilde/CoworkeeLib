using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Coworkee.Bff.Tests;

public sealed class CacheTicketStoreTests
{
    [Fact]
    public async Task The_sign_in_with_its_tokens_stays_on_the_server_until_removed()
    {
        var store = new CacheTicketStore(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        var properties = new AuthenticationProperties();
        properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = new string('a', 6000) }]);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1")], "test")), properties, "Cookies");

        var key = await store.StoreAsync(ticket);

        key.Length.ShouldBeLessThan(40);
        var loaded = (await store.RetrieveAsync(key))!;
        loaded.Principal.FindFirst("sub")!.Value.ShouldBe("u1");
        loaded.Properties.GetTokenValue("access_token")!.Length.ShouldBe(6000);
        await store.RemoveAsync(key);
        (await store.RetrieveAsync(key)).ShouldBeNull();
    }
}
