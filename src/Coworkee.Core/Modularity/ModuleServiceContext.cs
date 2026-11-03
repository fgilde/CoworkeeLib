using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Core.Modularity;

public sealed record ModuleServiceContext(IServiceCollection Services, IConfiguration Configuration);
