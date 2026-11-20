using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Testcontainers.Redis;

namespace Coworkee.Realtime.Tests;

/// <summary>
/// Container Apps and Kubernetes start Redis and the services side by side, so a service may come up before Redis answers. The instances join Redis
/// once it does: the shared connection kept trying instead of throwing at the start, which left presence, session and permission signals per instance.
/// </summary>
public sealed class RedisLateStartTests(RealtimeApp app) : IAsyncLifetime
{
    private readonly int _port = FreePort();
    private RedisContainer _redis = null!;
    private SetupResultDto _setup = null!;
    private WebApplication _first = null!;
    private WebApplication _second = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _redis = new RedisBuilder("redis:7-alpine").WithPortBinding(_port, 6379).Build();
        _setup = await app.SetupAsync();
        var redis = $"127.0.0.1:{_port},connectTimeout=1000";
        _first = await app.StartInstanceAsync(redis);
        _second = await app.StartInstanceAsync(redis);
    }

    public async ValueTask DisposeAsync()
    {
        await _first.DisposeAsync();
        await _second.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task Instances_started_before_redis_share_presence_once_it_answers()
    {
        await _redis.StartAsync(Ct);

        var deadline = DateTime.UtcNow.AddSeconds(60);
        Listener? onSecond = null;
        while (onSecond is null)
        {
            try
            {
                // the backplane connects with the first hub connection after Redis answers
                onSecond = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId, on: _second);
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500, Ct);
            }
        }

        await using (onSecond)
        {
            while (!(await OnlineAsync(_first)).Contains(_setup.AdminUserId))
            {
                DateTime.UtcNow.ShouldBeLessThan(deadline, "the first instance never saw the user of the second one: its presence stayed per instance");
                await Task.Delay(500, Ct);
            }
        }
    }

    private async Task<Guid[]> OnlineAsync(WebApplication instance) =>
        (await instance.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId).GetFromJsonAsync<Guid[]>(PresenceEvents.Path, Ct))!;

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
