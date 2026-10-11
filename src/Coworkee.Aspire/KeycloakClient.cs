using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>A confidential client of the app's realm; the realm gets its secret and redirect addresses on every start. Without redirect addresses any local one is allowed.</summary>
public sealed record KeycloakClient(string ClientId, IResourceBuilder<ParameterResource> Secret, IReadOnlyList<ReferenceExpression> RedirectUris);
