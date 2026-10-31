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
        options.Resolve(typeof(Mirrored))!.ParallelPages.ShouldBe(2);
        options.For<Mirrored>(s => (s.Expand, s.ParallelPages) = ("Brand,Owner", 4)).Resolve(typeof(Mirrored))!.ShouldSatisfyAllConditions(
            s => s.Expand.ShouldBe("Brand,Owner"),
            s => s.ParallelPages.ShouldBe(4));
    }

    [Coworkee.Domain.ClientEntity(Expand = "Brand")]
    private sealed class Mirrored;
}
