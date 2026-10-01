using System.Security.Claims;
using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Security;

public sealed class BffAuthenticationStateProvider(ICoworkeeApi api) : AuthenticationStateProvider
{
    private Task<AuthenticationState>? _state;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= LoadAsync();

    private async Task<AuthenticationState> LoadAsync()
    {
        try
        {
            var user = await api.GetUserAsync();
            if (!user.IsAuthenticated)
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            var claims = new List<Claim> { new("name", user.Name ?? user.Email ?? string.Empty), new("email", user.Email ?? string.Empty), new("sub", user.UserId?.ToString() ?? string.Empty) };
            claims.AddRange(user.Roles.Select(r => new Claim("role", r)));
            if (user.ManageUrl is { Length: > 0 } manage)
            {
                claims.Add(new Claim("manage_url", manage));
            }
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "bff", "name", "role")));
        }
        catch (ApiException)
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }
}
