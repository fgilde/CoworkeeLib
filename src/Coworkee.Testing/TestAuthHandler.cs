using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.Testing;

public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(SchemeName + " ", StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var parts = header[(SchemeName.Length + 1)..].Split(';');
        var claims = new List<Claim> { new("sub", parts[0]) };
        if (parts.Length > 1 && parts[1].Length > 0)
        {
            claims.Add(new Claim("tenant", parts[1]));
        }

        if (parts.Length > 2 && parts[2].Length > 0)
        {
            claims.Add(new Claim("exp", parts[2]));
        }

        // further parts are claims of their own: "Test {user};{tenant};;sid=abc;impersonator={id}"
        claims.AddRange(parts.Skip(3).Select(p => p.Split('=', 2)).Where(p => p.Length == 2).Select(p => new Claim(p[0], p[1])));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
