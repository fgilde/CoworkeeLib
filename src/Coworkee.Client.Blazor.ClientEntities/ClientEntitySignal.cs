namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>Tells the local copies that this client saved something, so they fetch the changes right away.</summary>
internal sealed class ClientEntitySignal
{
    public event Action? Mutated;

    public void RaiseMutated() => Mutated?.Invoke();
}

/// <summary>
/// After a successful write through any HTTP client the local copies refresh; signing out drops them from the browser storage,
/// so the next person on the device starts without them.
/// </summary>
internal sealed class ClientEntityHttpHandler(ClientEntitySignal signal, IClientEntityStorage storage, CoworkeeClientOptions options) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode || request.Method == HttpMethod.Get || request.Method == HttpMethod.Head || request.Method == HttpMethod.Options)
        {
            return response;
        }

        if (request.RequestUri?.AbsolutePath.EndsWith(options.LogoutPath.TrimStart('/'), StringComparison.OrdinalIgnoreCase) == true)
        {
            await storage.ClearAsync();
        }
        else
        {
            signal.RaiseMutated();
        }

        return response;
    }
}
