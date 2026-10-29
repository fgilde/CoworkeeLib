using System.Net;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;

namespace Coworkee.OData.Tests;

/// <summary>The service document and <c>$metadata</c> list every set and property name: they need a sign-in on every route, unless the app opts out.</summary>
public sealed class ODataDocumentsTests(ODataApp app)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Documents => new()
    {
        "/odata", "/odata/", "/odata/$metadata",
        "/rt/odata", "/rt/odata/$metadata",
        "/alias/odata", "/alias/odata/$metadata",
        "/hidden/odata", "/hidden/odata/$metadata",
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task Anonymous_requests_get_401_and_signed_in_users_the_document(string url)
    {
        var setup = await app.SetupAsync();

        (await app.App.GetTestClient().GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await app.As(setup.AdminUserId, setup.TenantId).GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnonymousMetadata_opens_the_documents_again()
    {
        await using var open = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:AnonymousMetadata"] = "true" });

        foreach (var url in new[] { "/odata", "/odata/$metadata", "/rt/odata/$metadata" })
        {
            (await open.GetTestClient().GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, url);
        }

        (await open.GetTestClient().GetAsync("/odata/Gadgets", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the sets keep their sign-in");
    }
}
