using Bunit;
using Coworkee.Client.Blazor.Customization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ComponentReplacementTests : ClientTestBase
{
    [Fact]
    public void A_replaced_component_renders_the_replacement_wherever_the_original_is_used()
    {
        Services.ReplaceComponent<Greeting, LoudGreeting>();

        var host = Render<GreetingHost>();

        host.Markup.ShouldBe("HELLO ADA");
    }

    [Fact]
    public void Replacements_chain_to_the_last_one()
    {
        var options = new ComponentReplacementOptions().Replace<Greeting, LoudGreeting>().Replace<LoudGreeting, QuietGreeting>();

        options.Resolve(typeof(Greeting)).ShouldBe(typeof(QuietGreeting));
        options.Resolve(typeof(GreetingHost)).ShouldBe(typeof(GreetingHost));
    }

    public class Greeting : ComponentBase
    {
        [Parameter] public string Name { get; set; } = string.Empty;

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, $"Hello {Name}");
    }

    public sealed class LoudGreeting : Greeting
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, $"HELLO {Name.ToUpperInvariant()}");
    }

    public sealed class QuietGreeting : Greeting
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, $"hello {Name.ToLowerInvariant()}");
    }

    public sealed class GreetingHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Greeting>(0);
            builder.AddComponentParameter(1, nameof(Greeting.Name), "Ada");
            builder.CloseComponent();
        }
    }
}
