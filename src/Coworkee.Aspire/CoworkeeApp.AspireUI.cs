using Aspire.Hosting.ApplicationModel;
using Nextended.Aspire.Hosting.AspireUI;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    internal IResourceBuilder<AspireUIResource> AddAspireUISignIn(IResourceBuilder<AspireUIResource> aspireUi, string defaultPermissions)
    {
        var client = AddKeycloakClient($"{Name}-{aspireUi.Resource.Name}", [ReferenceExpression.Create($"{aspireUi.GetEndpoint(AspireUIResource.HttpEndpointName)}/api/auth/sso/callback")]);
        // the app host owns the sign-in: AspireUI seeds settings only while they are empty, so a volume from an earlier realm or client would keep those
        return aspireUi.WithForcedSettings().WithSingleSignOn(ContainerKeycloakAuthority(), client.ClientId, client.Secret, Options.Keycloak!.DisplayName,
            groupsClaim: "roles", adminGroup: "Administrator", defaultPermissions: defaultPermissions);
    }
}
