using Coworkee.Infrastructure.Tokens;
using Coworkee.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Infrastructure.Tests;

public sealed class AccessTokenTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
    private readonly Guid _target = Guid.CreateVersion7();

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_single_use_token_opens_once_and_carries_who_issued_it()
    {
        await using var provider = database.CreateServices(_user, _clock);
        var token = await IssueAsync(provider, "export", singleUse: true);

        var grant = await RedeemAsync(provider, "export", token);
        grant.ShouldBe(new AccessTokenGrant(_target, _user.TenantId, _user.UserId));
        (await RedeemAsync(provider, "export", token)).ShouldBeNull();
    }

    [Fact]
    public async Task A_token_works_until_it_expires_and_only_for_its_purpose()
    {
        await using var provider = database.CreateServices(_user, _clock);
        var token = await IssueAsync(provider, "file", singleUse: false);

        (await RedeemAsync(provider, "file", token)).ShouldNotBeNull();
        (await RedeemAsync(provider, "file", token)).ShouldNotBeNull();
        (await RedeemAsync(provider, "export", token)).ShouldBeNull();
        (await RedeemAsync(provider, "file", token + "x")).ShouldBeNull();

        _clock.Advance(TimeSpan.FromMinutes(5));
        (await RedeemAsync(provider, "file", token)).ShouldBeNull();
    }

    [Fact]
    public async Task Only_the_hash_is_stored()
    {
        await using var provider = database.CreateServices(_user, _clock);
        var token = await IssueAsync(provider, "file", singleUse: false);

        await using var scope = provider.CreateAsyncScope();
        var row = scope.ServiceProvider.GetRequiredService<TestDbContext>().Set<AccessToken>().Single();
        row.Hash.ShouldNotContain(token);
        row.ExpiresAt.ShouldBe(_clock.GetUtcNow().AddMinutes(5));
    }

    private async Task<string> IssueAsync(IServiceProvider provider, string purpose, bool singleUse)
    {
        await using var scope = provider.CreateAsyncScope();
        var token = scope.ServiceProvider.GetRequiredService<IAccessTokens>().Issue(purpose, _target, TimeSpan.FromMinutes(5), singleUse);
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().SaveChangesAsync(Ct);
        return token;
    }

    private static async Task<AccessTokenGrant?> RedeemAsync(IServiceProvider provider, string purpose, string token)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccessTokens>().RedeemAsync(purpose, token, Ct);
    }
}
