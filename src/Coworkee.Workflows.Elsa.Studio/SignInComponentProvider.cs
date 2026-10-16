using Elsa.Studio.Contracts;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Workflows.Elsa.Studio;

internal sealed class SignInComponentProvider : IUnauthorizedComponentProvider
{
    public RenderFragment GetUnauthorizedComponent() => builder =>
    {
        builder.OpenComponent<SignIn>(0);
        builder.CloseComponent();
    };
}
