using System.Net;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;

namespace Coworkee.OData.Tests;

/// <summary>With <c>Coworkee:OData:CaseInsensitive</c> off, names in the URI match only in their own case.</summary>
public sealed class CaseSensitiveTests(ODataApp app)
{
    [Fact]
    public async Task Names_match_only_in_their_case_when_the_option_is_off()
    {
        var setup = await app.SetupAsync();
        await using var strict = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:CaseInsensitive"] = "false" });
        var client = strict.GetTestClient().AsUser(setup.AdminUserId, setup.TenantId);
        var ct = TestContext.Current.CancellationToken;

        (await client.GetAsync("/odata/Gadgets?$filter=Name eq 'Lamp'&$orderby=Price&$select=Name", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var query in new[] { "$filter=name eq 'Lamp'", "$orderby=price", "$select=name" })
        {
            (await client.GetAsync($"/odata/Gadgets?{query}", ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
        }

        (await app.As(setup.AdminUserId, setup.TenantId).GetAsync("/odata/Gadgets?$filter=name eq 'Lamp'", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
