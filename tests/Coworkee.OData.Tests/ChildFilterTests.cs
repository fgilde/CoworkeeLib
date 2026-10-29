using System.Net.Http.Json;
using System.Text.Json;

namespace Coworkee.OData.Tests;

public class ChildFilterTests(ODataApp app)
{
    /// <summary>The OData route the requests go to; <see cref="CamelCaseRoute"/> runs the suite again on a route that renames every property.</summary>
    protected virtual string Route => CoworkeeODataModule.RoutePrefix;

    private HttpClient As(Guid userId, Guid tenantId) => app.As(userId, tenantId, Route);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Children_of_a_parent_hidden_by_its_row_filter_are_hidden_too()
    {
        var setup = await app.SetupAsync();
        await app.InDbAsync(setup.TenantId, async db =>
        {
            var open = new Widget { Name = "Vise", Color = "Red", TenantId = setup.TenantId };
            var secret = new Widget { Name = "Safe", Color = "Secret", TenantId = setup.TenantId };
            db.AddRange(new Part { Name = "Jaw", Widget = open, TenantId = setup.TenantId }, new Part { Name = "Lock", Widget = secret, TenantId = setup.TenantId });
            return await db.SaveChangesAsync();
        });

        var page = await As(setup.AdminUserId, setup.TenantId).GetFromJsonAsync<JsonElement>("/odata/Parts?$count=true", Ct);

        page.GetProperty("@odata.count").GetInt32().ShouldBe(1);
        page.GetProperty("value").EnumerateArray().Select(p => p.GetProperty("Name").GetString()).ShouldBe(["Jaw"]);
    }
}
