using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace Coworkee.Client.Blazor.Customization;

internal sealed class ReplacingComponentActivator(IOptions<ComponentReplacementOptions> options) : IComponentActivator
{
    public IComponent CreateInstance(Type componentType) =>
        (IComponent)Activator.CreateInstance(options.Value.Resolve(componentType))!;
}
