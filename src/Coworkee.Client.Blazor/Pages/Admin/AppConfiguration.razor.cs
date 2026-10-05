using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class AppConfiguration
{
    [Inject] private IEnumerable<ClientAppConfiguration> Registrations { get; set; } = null!;
}
