using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer;

public sealed class CoworkeeAuthStoreModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<IModelContributor, AuthModelContributor>();
        context.Services.AddScoped<Application.Privacy.IPersonalDataContributor, AuthPersonalData>();
        context.Services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<CoworkeeDbContext>().ReplaceDefaultEntities<Guid>());
    }
}
