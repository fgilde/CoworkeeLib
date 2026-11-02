using Coworkee.Core.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nextended.Core.Attributes;
using Nextended.Core.Extensions;

namespace Coworkee.Core.Tests;

public sealed class RegisterAsTests
{
    [Fact]
    public void Module_assemblies_register_their_RegisterAs_types_with_lifetime()
    {
        var services = new ServiceCollection();

        services.AddCoworkeeModules<GreetingModule>(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IGreeter>().ShouldBeOfType<Greeter>();
        services.Single(d => d.ServiceType == typeof(IGreeter)).Lifetime.ShouldBe(ServiceLifetime.Scoped);
        provider.GetRequiredService<IClock>().ShouldBeSameAs(provider.GetRequiredService<IClock>());
    }

    [Fact]
    public void ReplaceServices_replaces_what_a_module_registered()
    {
        var services = new ServiceCollection();

        services.AddCoworkeeModules<GreetingModule>(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFormatter>().ShouldBeOfType<AttributeFormatter>();
        services.Count(d => d.ServiceType == typeof(IFormatter)).ShouldBe(1);
    }

    [Fact]
    public void Types_registered_already_by_the_same_attribute_are_not_added_twice()
    {
        var services = new ServiceCollection();
        services.RegisterAllWithRegisterAsAttribute(typeof(RegisterAsTests).Assembly);

        services.AddCoworkeeModules<GreetingModule>(new ConfigurationBuilder().Build());

        services.Count(d => d.ServiceType == typeof(IGreeter)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(IClock)).ShouldBe(1);
    }

    public interface IGreeter;

    public interface IClock;

    public interface IFormatter;

    [RegisterAs(typeof(IGreeter), ServiceLifetime = ServiceLifetime.Scoped)]
    internal sealed class Greeter : IGreeter;

    [RegisterAs(typeof(IClock), ServiceLifetime = ServiceLifetime.Singleton)]
    internal sealed class Clock : IClock;

    internal sealed class ModuleFormatter : IFormatter;

    [RegisterAs(typeof(IFormatter), ReplaceServices = true)]
    internal sealed class AttributeFormatter : IFormatter;

    private sealed class GreetingModule : CoworkeeModule
    {
        public override void ConfigureServices(ModuleServiceContext context) => context.Services.AddSingleton<IFormatter, ModuleFormatter>();
    }
}
