using Coworkee.Core.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Core.Tests;

public sealed class ModuleLoaderTests
{
    [Fact]
    public void Resolves_dependencies_first_and_once() =>
        ModuleLoader.Resolve(typeof(AppModule)).ShouldBe([typeof(BaseModule), typeof(LeftModule), typeof(RightModule), typeof(AppModule)]);

    [Fact]
    public void Detects_cycles_with_path()
    {
        var ex = Should.Throw<InvalidOperationException>(() => ModuleLoader.Resolve(typeof(CycleA)));

        ex.Message.ShouldContain("CycleA -> CycleB -> CycleA");
    }

    [Fact]
    public void Rejects_non_module_types() =>
        Should.Throw<InvalidOperationException>(() => ModuleLoader.Resolve(typeof(string)));

    [Fact]
    public void Configures_services_in_dependency_order()
    {
        var services = new ServiceCollection();

        services.AddCoworkeeModules<AppModule>(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        provider.GetServices<string>().ShouldBe(["Base", "Left", "Right", "App"]);
        provider.GetRequiredService<IReadOnlyList<CoworkeeModule>>().Count.ShouldBe(4);
    }

    private abstract class NamedModule(string name) : CoworkeeModule
    {
        public override void ConfigureServices(ModuleServiceContext context) => context.Services.AddSingleton(name);
    }

    private sealed class BaseModule() : NamedModule("Base");

    [DependsOn(typeof(BaseModule))]
    private sealed class LeftModule() : NamedModule("Left");

    [DependsOn(typeof(BaseModule))]
    private sealed class RightModule() : NamedModule("Right");

    [DependsOn(typeof(LeftModule), typeof(RightModule))]
    private sealed class AppModule() : NamedModule("App");

    [DependsOn(typeof(CycleB))]
    private sealed class CycleA : CoworkeeModule;

    [DependsOn(typeof(CycleA))]
    private sealed class CycleB : CoworkeeModule;
}
