using System.Text.Json;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Http;

namespace Coworkee.AuthServer;

/// <summary>
/// The sign-in page with an unsaved <see cref="SignInDesign"/>, for the preview on the Themes page: <c>GET /Account/Preview?design={json}</c> renders the
/// real sign-in page with that design. Only inside a frame (Sec-Fetch-Dest: iframe) of the registered clients' origins (frame-ancestors), so no link
/// or other site can show a crafted design under the auth server's address; the design passes the same checks as a saved one.
/// </summary>
public static class SignInPreview
{
    public const string Path = "/Account/Preview";

    private const string Key = "coworkee:sign-in-preview";

    /// <summary>The checked design of a preview request; false (answer 404) for a preview request that is not a GET from a frame or has no valid design.</summary>
    public static bool TryStart(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Headers["Sec-Fetch-Dest"] != "iframe"
            || context.Request.Query["design"].ToString() is not { Length: > 0 and <= 8192 } json)
        {
            return false;
        }

        try
        {
            context.Items[Key] = AuthBrandingProvider.Checked(JsonSerializer.Deserialize<SignInDesign>(json, JsonSerializerOptions.Web) ?? new SignInDesign());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsPreview(HttpContext context) => context.Request.Path.Equals(Path, StringComparison.OrdinalIgnoreCase);

    internal static SignInDesign? Design(HttpContext? context) => context?.Items[Key] as SignInDesign;
}
