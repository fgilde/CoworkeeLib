using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace Coworkee.Workflows.Elsa.Studio.Areas.ElsaStudio.Pages;

/// <summary>Signs the visitor in and lets only users the Elsa API accepts (permission Workflows.Elsa.Manage) into the designer.</summary>
public sealed class HostModel(IHttpClientFactory clients, IConfiguration configuration) : PageModel
{
    public string StylesBundle { get; } = $"{Assembly.GetEntryAssembly()?.GetName().Name}.styles.css";

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge(new AuthenticationProperties { RedirectUri = Request.Path + Request.QueryString });
        }

        var backend = ElsaStudioExtensions.BackendUrl(configuration);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{backend}/features/installed");
        request.Headers.Authorization = new("Bearer", await HttpContext.GetTokenAsync("access_token"));
        using var response = await clients.CreateClient(ElsaStudioExtensions.PermissionCheckClient).SendAsync(request, HttpContext.RequestAborted);
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Challenge(new AuthenticationProperties { RedirectUri = Request.Path + Request.QueryString });
            case HttpStatusCode.Forbidden:
                return new ContentResult
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                    ContentType = "text/html; charset=utf-8",
                    Content = "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Workflow designer</title></head>"
                        + "<body style=\"font-family:sans-serif;margin:3rem\"><h1>Workflow designer</h1>"
                        + "<p>Your account lacks the permission <code>Workflows.Elsa.Manage</code>. Ask an administrator for it.</p></body></html>",
                };
            default:
                return Page();
        }
    }
}
