using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.ExtendedAttributes.Tests;

public sealed class KeyedEntityTests
{
    [Fact]
    public void An_entity_without_a_guid_id_is_refused_at_registration()
    {
        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddExtendedAttributes<Coded>("Codes", "View", "Edit"))
            .Message.ShouldContain("Guid Id");
    }

    private sealed class Coded
    {
        public required string Code { get; set; }
    }
}
