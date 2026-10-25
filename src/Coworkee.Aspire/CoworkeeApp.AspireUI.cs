using Aspire.Hosting.ApplicationModel;
using Nextended.Aspire.Hosting.AspireUI;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    /// <summary>The administrator of the app signs in; in a deployment AspireUI gets a public address, with Keycloak the single sign-on.</summary>
    internal IResourceBuilder<AspireUIResource> AddAspireUI(IResourceBuilder<AspireUIResource> aspireUi, string defaultPermissions)
    {
        // the default volume is named after the resource only: every app on the machine would share it, and the last one started would set the sign-in of all
        foreach (var volume in aspireUi.Resource.Annotations.OfType<ContainerMountAnnotation>().Where(m => m.Target == "/data").ToList())
        {
            aspireUi.Resource.Annotations.Remove(volume);
        }

        aspireUi.WithVolume($"{Name}-{aspireUi.Resource.Name}-data", "/data");
        if (Publishing)
        {
            ToolUrl(aspireUi, AspireUIResource.HttpEndpointName);
            aspireUi.WithEnvironment("ASPIREUI_ADMIN_USERNAME", AdminEmail).WithEnvironment("ASPIREUI_ADMIN_PASSWORD", AdminPassword);
        }
        else
        {
            aspireUi.WithAdminUser(AdminUser, AdminPassword);
        }

        return Keycloak is null ? aspireUi : AddAspireUISignIn(aspireUi, defaultPermissions);
    }

    internal IResourceBuilder<AspireUIResource> AddAspireUISignIn(IResourceBuilder<AspireUIResource> aspireUi, string defaultPermissions)
    {
        var client = AddKeycloakClient($"{Name}-{aspireUi.Resource.Name}", [ReferenceExpression.Create($"{ToolUrl(aspireUi, AspireUIResource.HttpEndpointName)}/api/auth/sso/callback")]);
        // the app host owns the sign-in: AspireUI seeds settings only while they are empty, so a volume from an earlier realm or client would keep those
        return aspireUi.WithForcedSettings().WithSingleSignOn(ContainerKeycloakAuthority(), client.ClientId, client.Secret, Options.Keycloak!.DisplayName,
            groupsClaim: "roles", adminGroup: "Administrator", defaultPermissions: defaultPermissions);
    }
}
