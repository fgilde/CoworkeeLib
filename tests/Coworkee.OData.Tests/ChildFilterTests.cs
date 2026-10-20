using System.Net.Http.Json;
using System.Text.Json;

namespace Coworkee.OData.Tests;

public sealed class ChildFilterTests(ODataApp app)
{
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

        var page = await app.As(setup.AdminUserId, setup.TenantId).GetFromJsonAsync<JsonElement>("/odata/Parts?$count=true", Ct);

        page.GetProperty("@odata.count").GetInt32().ShouldBe(1);
        page.GetProperty("value").EnumerateArray().Select(p => p.GetProperty("Name").GetString()).ShouldBe(["Jaw"]);
    }
}
