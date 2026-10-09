using Coworkee.Application;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Realtime;

[DependsOn(typeof(CoworkeeApplicationModule))]
public sealed class CoworkeeRealtimeModule : CoworkeeModule, IWebModule
{
    public const string RedisConnectionStringName = "redis";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        var signalR = services.AddSignalR();
        if (context.Configuration.GetConnectionString(RedisConnectionStringName) is { Length: > 0 } redis)
        {
            signalR.AddStackExchangeRedis(redis, options => options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("coworkee"));
            if (string.Equals(context.Configuration[RedisDistributedLock.ProviderSetting], "Redis", StringComparison.OrdinalIgnoreCase))
            {
                services.TryAddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ => StackExchange.Redis.ConnectionMultiplexer.Connect(redis));
                services.AddSingleton<IDistributedLock, RedisDistributedLock>();
            }
        }

        services.AddSingleton<IRealtimePublisher, RealtimePublisher>();
        services.AddScoped<IInterceptor, RealtimeChangeInterceptor>();
        services.AddScoped<IRealtimeTopicAuthorizer, UserTopicAuthorizer>();
        services.AddScoped<IRealtimeTopicAuthorizer, GlobalTopicAuthorizer>();
        services.AddScoped<IRealtimeTopicAuthorizer, TenantTopicAuthorizer>();
        services.AddScoped<IRealtimeTopicAuthorizer, EntityTopicAuthorizer>();
    }

    public void ConfigureApplication(WebApplication app) => app.MapHub<RealtimeHub>(RealtimeHubMethods.Path);
}
