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
}
