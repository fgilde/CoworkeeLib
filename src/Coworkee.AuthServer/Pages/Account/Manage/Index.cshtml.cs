using Coworkee.Contracts.Configuration;
using Coworkee.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer.Pages.Account.Manage;

[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class IndexModel(ISettingProvider settings, ISettingDefinitionManager definitions, IOptions<AuthServerOptions> options) : PageModel
{
    public bool ShowsPasskeys { get; private set; }

    public async Task OnGetAsync() => ShowsPasskeys = await Passkeys.EnabledAsync(settings, definitions, options.Value, HttpContext.RequestAborted);
}
