using System.Security.Claims;
using Coworkee.Application.Authorization;
using Coworkee.Application.Caching;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Tests;

/// <summary>
/// A cached query's load outlives the request that started it: its handler acts as that caller (key and its scopes, "Log in as", service client),
/// and an entry is shared only by callers with the same rights.
/// </summary>
public sealed class CachedQueryIdentityTests(IdentityApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Claim[] Key(Guid key) =>
        [new(ApiKeyDefaults.IdClaim, key.ToString()), new(ApiKeyDefaults.ScopeClaim, IdentityPermissions.Users.View)];

    [Fact]
    public async Task A_key_whose_request_ends_first_keeps_its_scopes_and_a_session_gets_its_own_rights()
    {
        var key = Key(Guid.CreateVersion7());
        var gate = HeldProbe.Arm();
        using var aborted = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        // a key's request starts the load and is held in it, a second request with the key joins, the user's session asks too
        var first = Task.Run(() => SendAsync(new ProbeRights("rights"), User(key), aborted.Token), Ct);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var joined = Task.Run(() => SendAsync(new ProbeRights("rights"), User(key), Ct), Ct);
        var session = Task.Run(() => SendAsync(new ProbeRights("rights"), User(), Ct), Ct);
        await Task.Delay(200, Ct); // both reach the cache

        // the first request ends; the load goes on for the second one
        await aborted.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(first);
        gate.Released.SetResult();

        (await joined.WaitAsync(TimeSpan.FromSeconds(30), Ct)).Value.ShouldBeFalse("the key's scopes stop Roles.View");
        (await session.WaitAsync(TimeSpan.FromSeconds(30), Ct)).Value.ShouldBeTrue();
        (await SendAsync(new ProbeRights("rights"), User(key), Ct)).Value.ShouldBeFalse();
    }

    [Fact]
    public async Task The_sessions_entry_is_not_served_to_a_narrower_key_or_a_log_in_as()
    {
        (await SendAsync(new ProbeRights("narrow"), User(), Ct)).Value.ShouldBeTrue();

        (await SendAsync(new ProbeRights("narrow"), User(Key(Guid.CreateVersion7())), Ct)).Value.ShouldBeFalse();
        var impersonator = Guid.CreateVersion7();
        (await SendAsync(new ProbeIdentity("as", CacheScope.User), User(new Claim(ImpersonationClaims.Impersonator, impersonator.ToString())), Ct))
            .Value.ShouldContain($"impersonator={impersonator}");
        (await SendAsync(new ProbeIdentity("as", CacheScope.User), User(), Ct)).Value.ShouldContain("impersonator=-");
    }

    [Fact]
    public async Task The_handler_acts_as_a_log_in_as_session_after_its_request_ended()
    {
        var impersonator = Guid.CreateVersion7();
        var seen = await HeldAsync(User(new Claim(ImpersonationClaims.Impersonator, impersonator.ToString())));

        seen.ShouldBe($"user={_setup.AdminUserId};impersonator={impersonator};client=-;permissions=;key=-;scopes=-;interactive=True");
    }

    [Fact]
    public async Task The_handler_acts_as_a_service_client_after_its_request_ended()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new("sub", "reporter"), new("client_id", "reporter"), new("tenant", _setup.TenantId.ToString()), new("permission", IdentityPermissions.Users.View)], "Test"));

        var seen = await HeldAsync(principal);

        seen.ShouldBe($"user=-;impersonator=-;client=reporter;permissions={IdentityPermissions.Users.View};key=-;scopes=-;interactive=False");
    }

    /// <summary>The identity the handler saw when the request that started the load ended before it, and another one waited on it.</summary>
    private async Task<string> HeldAsync(ClaimsPrincipal principal)
    {
        var query = new ProbeIdentity(Guid.CreateVersion7().ToString(), CacheScope.Tenant);
        var gate = HeldProbe.Arm();
        using var aborted = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var first = Task.Run(() => SendAsync(query, principal, aborted.Token), Ct);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var joined = Task.Run(() => SendAsync(query, principal, Ct), Ct);
        await Task.Delay(200, Ct); // the second request reaches the cache and joins the load

        await aborted.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(first);
        gate.Released.SetResult();
        return (await joined.WaitAsync(TimeSpan.FromSeconds(30), Ct)).Value;
    }

    private ClaimsPrincipal User(params Claim[] extra) =>
        new(new ClaimsIdentity([new Claim("sub", _setup.AdminUserId.ToString()), new Claim("tenant", _setup.TenantId.ToString()), .. extra], "Test"));

    /// <summary>A request: its scope and HttpContext live until it ends, then the context is gone as after a real request.</summary>
    private async Task<T> SendAsync<T>(IRequest<T> request, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = principal, RequestServices = scope.ServiceProvider };
        try
        {
            return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(request, cancellationToken);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }
}

/// <summary>Whether the caller holds Roles.View, cached per user.</summary>
public sealed record ProbeRights(string Key) : IQuery<Result<bool>>, ICachedQuery
{
    public string CacheKey => Key;

    public CacheScope CacheScope => CacheScope.User;
}

/// <summary>Who the handler acts as.</summary>
public sealed record ProbeIdentity(string Key, CacheScope Scope) : IQuery<Result<string>>, ICachedQuery
{
    public string CacheKey => Key;

    public CacheScope CacheScope => Scope;
}

/// <summary>Holds the next probe until released.</summary>
public static class HeldProbe
{
    private static Held? _next;

    public static Held Arm() => _next = new Held();

    internal static async Task WaitAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _next, null) is { } held)
        {
            held.Entered.TrySetResult();
            await held.Released.Task.WaitAsync(cancellationToken);
        }
    }

    public sealed class Held
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

internal sealed class ProbeHandlers(IPermissionChecker permissions, ICurrentUser user) : IHandler<ProbeRights, Result<bool>>, IHandler<ProbeIdentity, Result<string>>
{
    public async Task<Result<bool>> HandleAsync(ProbeRights query, CancellationToken cancellationToken)
    {
        await HeldProbe.WaitAsync(cancellationToken);
        return await permissions.IsGrantedAsync(IdentityPermissions.Roles.View, cancellationToken);
    }

    public async Task<Result<string>> HandleAsync(ProbeIdentity query, CancellationToken cancellationToken)
    {
        await HeldProbe.WaitAsync(cancellationToken);
        return $"user={(object?)user.UserId ?? "-"};impersonator={(object?)user.ImpersonatorId ?? "-"};client={user.ClientId ?? "-"};"
            + $"permissions={string.Join(',', user.ClientPermissions)};key={(object?)user.ApiKeyId ?? "-"};"
            + $"scopes={(user.PermissionScopes is { } scopes ? string.Join(',', scopes) : "-")};interactive={user.IsInteractive}";
    }
}
