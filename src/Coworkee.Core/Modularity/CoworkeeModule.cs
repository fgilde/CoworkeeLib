using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Core.Modularity;

public abstract class CoworkeeModule
{
    public virtual void ConfigureServices(ModuleServiceContext context)
    {
    }
}

public sealed record ModuleServiceContext(IServiceCollection Services, IConfiguration Configuration);
