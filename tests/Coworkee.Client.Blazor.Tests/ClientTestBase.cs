using Bunit;
using Coworkee.Client.Blazor;
using Coworkee.Client.Blazor.Api;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public abstract class ClientTestBase : BunitContext
{
    protected ClientTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(Api);
        Services.AddSingleton(new CoworkeeClientOptions());
        Services.AddScoped<Theming.ThemeService>();
    }

    protected ICoworkeeApi Api { get; } = Substitute.For<ICoworkeeApi>();
}
