using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.ClientEntities;

internal sealed class ClientEntityApi(HttpClient http) : ApiClientBase(http)
{
    private const string Root = "api/v1/client-entities";

    public Task<ClientEntityManifest> ManifestAsync(CancellationToken cancellationToken) => GetAsync<ClientEntityManifest>(Root, cancellationToken);

    /// <summary>With a <paramref name="skip"/> the changed ids come in pages (<see cref="ClientEntityChanges.More"/>) instead of a reset.</summary>
    public Task<ClientEntityChanges> ChangesAsync(string entitySet, DateTimeOffset? since, int? skip, CancellationToken cancellationToken)
    {
        var query = since is null ? "" : "?since=" + Uri.EscapeDataString(since.Value.ToString("O")) + (skip is null ? "" : "&skip=" + skip.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return GetAsync<ClientEntityChanges>($"{Root}/{Uri.EscapeDataString(entitySet)}/changes{query}", cancellationToken);
    }

    /// <summary>PoC: the copy as a SQLite file built by the server, and the version it was taken at; null when the server has no snapshots.</summary>
    public async Task<(byte[] File, DateTimeOffset Version)?> SnapshotAsync(string entitySet, ClientEntitySnapshotRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendCoreAsync(HttpMethod.Post, $"{Root}/{Uri.EscapeDataString(entitySet)}/snapshot", request, cancellationToken);
            var version = DateTimeOffset.Parse(response.Headers.GetValues(ClientEntitySnapshotKinds.VersionHeader).First(), System.Globalization.CultureInfo.InvariantCulture);
            // the browser unpacked the gzip already
            return (await response.Content.ReadAsByteArrayAsync(cancellationToken), version);
        }
        catch (Api.ApiException exception) when (exception.Status is 404 or 405)
        {
            return null;
        }
    }

    public Task<Guid[]> IdsAsync(string entitySet, CancellationToken cancellationToken) => GetAsync<Guid[]>($"{Root}/{Uri.EscapeDataString(entitySet)}/ids", cancellationToken);
}
