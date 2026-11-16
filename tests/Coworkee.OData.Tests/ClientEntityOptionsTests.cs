using Coworkee.Contracts.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.OData.Tests;

public sealed class ClientEntityOptionsTests
{
    [Fact]
    public void The_attribute_enables_with_the_defaults_and_options_override_or_disable()
    {
        var options = new ClientEntityOptions { Default = { MaxRows = 10 } };

        options.Resolve(typeof(Gadget))!.MaxRows.ShouldBe(10);
        options.Resolve(typeof(AddGadgetCommand)).ShouldBeNull();
        options.For<AddGadgetCommand>(s => s.BatchSize = 5).Resolve(typeof(AddGadgetCommand))!.BatchSize.ShouldBe(5);
        options.For<Gadget>(s => s.Enabled = false).Resolve(typeof(Gadget)).ShouldBeNull();
        options.Default.BatchSize.ShouldBe(1000);
    }

    [Fact]
    public void The_attribute_and_the_options_name_the_expand()
    {
        var options = new ClientEntityOptions();

        options.Resolve(typeof(Mirrored))!.Expand.ShouldBe("Brand");
        options.Resolve(typeof(Mirrored))!.ParallelPages.ShouldBe(1);
        options.For<Mirrored>(s => (s.Expand, s.ParallelPages) = ("Brand,Owner", 4)).Resolve(typeof(Mirrored))!.ShouldSatisfyAllConditions(
            s => s.Expand.ShouldBe("Brand,Owner"),
            s => s.ParallelPages.ShouldBe(4));
    }

    [Fact]
    public void An_override_turns_a_marked_set_off_an_unmarked_one_on_and_changes_its_values()
    {
        var options = new ClientEntityOptions();
        options.Sets["gadgets"] = new ClientEntitySetOverride { Copy = ClientEntityCopy.Off };
        options.Sets["Commands"] = new ClientEntitySetOverride { Copy = ClientEntityCopy.Sqlite, MaxRows = 9, RefreshInterval = TimeSpan.FromSeconds(30), DefaultMode = ClientEntityDefaultMode.Server };
        options.Sets["Mirrored"] = new ClientEntitySetOverride { BatchSize = 20 };

        options.Resolve(typeof(Gadget), "Gadgets").ShouldBeNull();
        options.Resolve(typeof(Gadget))!.Copy.ShouldBeNull();
        options.Resolve(typeof(AddGadgetCommand), "Commands")!.ShouldSatisfyAllConditions(
            s => s.Copy.ShouldBe(ClientEntityCopy.Sqlite),
            s => s.MaxRows.ShouldBe(9),
            s => s.RefreshInterval.ShouldBe(TimeSpan.FromSeconds(30)),
            s => s.DefaultMode.ShouldBe(ClientEntityDefaultMode.Server));
        options.Resolve(typeof(Mirrored), "Mirrored")!.ShouldSatisfyAllConditions(s => s.BatchSize.ShouldBe(20), s => s.Copy.ShouldBeNull());
        // values without a copy do not mirror an unmarked set
        options.Sets["Commands"].Copy = null;
        options.Resolve(typeof(AddGadgetCommand), "Commands").ShouldBeNull();
    }

    [Fact]
    public void Describe_tells_what_decided_each_value()
    {
        var options = new ClientEntityOptions { Default = { BatchSize = 300 } }.For<Mirrored>(s => s.ParallelPages = 3);
        options.ForService(typeof(Mirrored), s => s.MaxRows = 77);
        options.Sets["Mirrored"] = new ClientEntitySetOverride { RefreshInterval = TimeSpan.FromMinutes(1), Copy = ClientEntityCopy.Memory };

        var (settings, sources) = options.Describe(typeof(Mirrored), "Mirrored");
        var (unmarked, unmarkedSources) = options.Describe(typeof(AddGadgetCommand), "Commands");
        var (gadget, gadgetSources) = new ClientEntityOptions().Describe(typeof(Gadget), "Gadgets");

        (settings.Enabled, settings.Copy, settings.BatchSize, settings.ParallelPages, settings.MaxRows).ShouldBe((true, ClientEntityCopy.Memory, 300, 3, 77));
        sources.ShouldBe(new Dictionary<string, ClientEntitySettingSource>
        {
            ["Copy"] = ClientEntitySettingSource.Override, ["MaxRows"] = ClientEntitySettingSource.Service, ["BatchSize"] = ClientEntitySettingSource.Default,
            ["ParallelPages"] = ClientEntitySettingSource.Code, ["RefreshInterval"] = ClientEntitySettingSource.Override, ["DefaultMode"] = ClientEntitySettingSource.Default,
        }, ignoreOrder: true);
        unmarked.Enabled.ShouldBeFalse();
        unmarkedSources["Copy"].ShouldBe(ClientEntitySettingSource.Default);
        (gadget.Enabled, gadgetSources["Copy"], gadgetSources["MaxRows"]).ShouldBe((true, ClientEntitySettingSource.Attribute, ClientEntitySettingSource.Default));
    }

    [Fact]
    public void Overrides_in_the_configuration_apply_live_and_the_code_stays()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var monitor = new ServiceCollection()
            .Configure<ClientEntityOptions>(configuration.GetSection(ClientEntityOptions.Section))
            .AddCoworkeeClientEntities(o => o.For<Mirrored>(s => s.MaxRows = 12))
            .BuildServiceProvider().GetRequiredService<IOptionsMonitor<ClientEntityOptions>>();
        monitor.CurrentValue.Resolve(typeof(Gadget), "Gadgets").ShouldNotBeNull();

        configuration["Coworkee:ClientEntities:Sets:Gadgets:Copy"] = "Off";
        configuration["Coworkee:ClientEntities:Sets:Mirrored:Copy"] = "Sqlite";
        configuration["Coworkee:ClientEntities:Sets:Mirrored:RefreshInterval"] = "00:00:45";
        configuration.Reload();

        monitor.CurrentValue.Resolve(typeof(Gadget), "Gadgets").ShouldBeNull();
        monitor.CurrentValue.Resolve(typeof(Mirrored), "mirrored")!.ShouldSatisfyAllConditions(
            s => s.Copy.ShouldBe(ClientEntityCopy.Sqlite),
            s => s.RefreshInterval.ShouldBe(TimeSpan.FromSeconds(45)),
            s => s.MaxRows.ShouldBe(12));
    }

    [Coworkee.Domain.ClientEntity(Expand = "Brand")]
    private sealed class Mirrored;
}
