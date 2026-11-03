using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Security;

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
