using Coworkee.Application.Messaging;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Application;

public sealed class CoworkeeApplicationModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddDispatcher();
        services.AddScoped<IRequestMiddleware, LoggingMiddleware>();
        services.AddScoped<IRequestMiddleware, ValidationMiddleware>();
        services.AddScoped<IRequestMiddleware, UnitOfWorkMiddleware>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();
    }
}
