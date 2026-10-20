using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Security;

/// <summary>The signed-in user as the client knows him (from <c>/bff/user</c>); anonymous when nobody is signed in.</summary>
public sealed record ClientUser(bool IsAuthenticated, Guid? UserId, Guid? TenantId, string? Email, string? DisplayName)
{
    public const string TenantClaimType = "tenant_id";

    public static readonly ClientUser Anonymous = new(false, null, null, null, null);

    public static ClientUser From(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated != true
            ? Anonymous
            : new(true, Id(user, "sub"), Id(user, TenantClaimType), NonEmpty(user.FindFirst("email")?.Value), NonEmpty(user.Identity.Name));

    public bool TryGetUserId(out Guid userId)
    {
        userId = UserId ?? Guid.Empty;
        return UserId is not null;
    }

    private static Guid? Id(ClaimsPrincipal user, string type) => Guid.TryParse(user.FindFirst(type)?.Value, out var id) ? id : null;

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

/// <summary>Scoped access to the signed-in user for pages and services without the cascading authentication state; <see cref="Changed"/> fires on sign-in, sign-out and reissues.</summary>
public sealed class CurrentUserInfo : IDisposable
{
    private readonly AuthenticationStateProvider _authentication;

    public CurrentUserInfo(AuthenticationStateProvider authentication)
    {
        _authentication = authentication;
        authentication.AuthenticationStateChanged += OnChanged;
    }

    public event Action<ClientUser>? Changed;

    public async Task<ClientUser> GetAsync() => ClientUser.From((await _authentication.GetAuthenticationStateAsync()).User);

    public void Dispose() => _authentication.AuthenticationStateChanged -= OnChanged;

    private async void OnChanged(Task<AuthenticationState> state)
    {
        try
        {
            Changed?.Invoke(ClientUser.From((await state).User));
        }
        catch (Exception)
        {
            // a failed load leaves the last user; the provider reports the error itself
        }
    }
}
