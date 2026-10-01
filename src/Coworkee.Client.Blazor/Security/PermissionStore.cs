using Coworkee.Client.Blazor.Api;

namespace Coworkee.Client.Blazor.Security;

public sealed class PermissionStore(ICoworkeeApi api)
{
    private Task<HashSet<string>>? _permissions;

    public event Action? Changed;

    public async Task<bool> HasAsync(string permission) => (await (_permissions ??= LoadAsync())).Contains(permission);

    public void Reset()
    {
        _permissions = null;
        Changed?.Invoke();
    }

    private async Task<HashSet<string>> LoadAsync()
    {
        try
        {
            return new HashSet<string>(await api.GetMyPermissionsAsync(), StringComparer.Ordinal);
        }
        catch (ApiException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
