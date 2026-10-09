using System.Collections.Immutable;
using System.Text.Json;
using OpenIddict.Abstractions;

namespace Coworkee.AuthServer.Clients;

/// <summary>What the app launcher shows of a client, stored as property of the OpenIddict application.</summary>
internal sealed record ClientApp(string? ClientUri, string? LogoUrl, string? Description, bool ShowInLauncher)
{
    private const string Property = "coworkee_app";

    public static readonly ClientApp None = new(null, null, null, false);

    public static ClientApp Read(ImmutableDictionary<string, JsonElement> properties) =>
        properties.TryGetValue(Property, out var value) ? value.Deserialize<ClientApp>() ?? None : None;

    public static ClientApp Of(string? clientUri, string? logoUrl, string? description, bool showInLauncher) =>
        new(Trimmed(clientUri), Trimmed(logoUrl), Trimmed(description), showInLauncher);

    public static bool IsWebAddress(string? uri) => Uri.TryCreate(uri, UriKind.Absolute, out var address) && address.Scheme is "https" or "http";

    public void Write(OpenIddictApplicationDescriptor descriptor)
    {
        descriptor.Properties.Remove(Property);
        if (this != None)
        {
            descriptor.Properties[Property] = JsonSerializer.SerializeToElement(this);
        }
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
