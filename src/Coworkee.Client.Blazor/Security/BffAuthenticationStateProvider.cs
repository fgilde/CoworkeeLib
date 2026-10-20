using System.Security.Claims;
using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Security;

public sealed class BffAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly ICoworkeeApi _api;
    private readonly PermissionStore _permissions;
    private Task<AuthenticationState>? _state;

    public BffAuthenticationStateProvider(ICoworkeeApi api, PermissionStore permissions)
    {
        _api = api;
        _permissions = permissions;
        permissions.Changed += Reevaluate;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= LoadAsync();

    public void Dispose() => _permissions.Changed -= Reevaluate;

    // the same user with other permissions: AuthorizeView and the router check the policies again
    private void Reevaluate()
    {
        if (_state is { } state)
        {
            NotifyAuthenticationStateChanged(ReissueAsync(state));
        }
    }

    private static async Task<AuthenticationState> ReissueAsync(Task<AuthenticationState> state) => new((await state).User);

    private async Task<AuthenticationState> LoadAsync()
    {
        try
        {
            var user = await _api.GetUserAsync();
            if (!user.IsAuthenticated)
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            var claims = new List<Claim> { new("name", user.Name ?? user.Email ?? string.Empty), new("email", user.Email ?? string.Empty), new("sub", user.UserId?.ToString() ?? string.Empty) };
            if (user.TenantId is { } tenantId)
            {
                claims.Add(new Claim(ClientUser.TenantClaimType, tenantId.ToString()));
            }

            claims.AddRange(user.Roles.Select(r => new Claim("role", r)));
            if (user.ManageUrl is { Length: > 0 } manage)
            {
                claims.Add(new Claim("manage_url", manage));
            }

            if (user.SystemTenant is { } system)
            {
                claims.Add(new Claim(SystemTenant.ClaimType, system ? "true" : "false"));
            }

            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "bff", "name", "role")));
        }
        catch (ApiException)
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }
}
