using Coworkee.AspNetCore.ApplicationServices;
using Coworkee.OData.ClientEntities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.OData.Tests;

public interface IGadgetAppService;

internal sealed class GadgetAppService : IGadgetAppService;

public sealed class ServiceClientEntityTests
{
    [Fact]
    public void A_service_names_its_entity_and_its_settings_apply_last()
    {
        ApplicationServiceRegistry.Add<IGadgetAppService, GadgetAppService>("/api/v1/gadgets", "Gadgets", _ => { });
        var services = new ServiceCollection()
            .AddCoworkeeApplicationServices(typeof(ServiceClientEntityTests).Assembly)
            .AddSingleton<IPostConfigureOptions<ClientEntityOptions>, ServiceClientEntities>()
            .Configure<ApplicationServiceOptions>(o => o.Configure<IGadgetAppService>(s => s.ClientEntity<AddGadgetCommand>(c => c.MaxRows = 7)))
            .AddCoworkeeClientEntities(o => o.For<AddGadgetCommand>(c => (c.MaxRows, c.BatchSize) = (5, 50)))
            .BuildServiceProvider();

        var settings = services.GetRequiredService<IOptions<ClientEntityOptions>>().Value.Resolve(typeof(AddGadgetCommand))!;

        (settings.MaxRows, settings.BatchSize).ShouldBe((7, 50));
    }
}
