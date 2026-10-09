using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer;

/// <summary>The OpenIddict stores, wherever the database is used: the auth server and the API, which manages clients and revokes sessions.</summary>
public sealed class CoworkeeAuthStoreModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<IModelContributor, AuthModelContributor>();
        context.Services.AddScoped<Application.Privacy.IPersonalDataContributor, AuthPersonalData>();
        AddClientAdministration(context.Services);
        context.Services.AddScoped<Clients.HostAccess>();
        context.Services.AddScoped<Identity.Users.IUserSessionListener, Clients.SessionRevocation>();
        context.Services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<CoworkeeDbContext>().ReplaceDefaultEntities<Guid>());
    }

    public void ConfigureApplication(WebApplication app) => Clients.ClientEndpoints.Map(app);

    // only the handlers and validators of the client pages: the account pages' validators need the auth server's options
    private static void AddClientAdministration(IServiceCollection services)
    {
        foreach (var type in new[] { typeof(Clients.ClientHandlers), typeof(Clients.ScopeHandlers) })
        {
            foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHandler<,>)))
            {
                services.AddScoped(contract, type);
            }
        }

        services.AddValidatorsFromAssembly(typeof(CoworkeeAuthStoreModule).Assembly, ServiceLifetime.Scoped,
            result => result.ValidatorType.Namespace == typeof(Clients.ClientHandlers).Namespace, includeInternalTypes: true);
    }
}
