using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

/// <summary>
/// <c>Coworkee:OData:ModelFromEntityFramework</c>: keys and properties come from the EF model of an external context. The database computes
/// <see cref="Consignment.DoubleWeight"/> (no public setter); <see cref="Pallet"/> has <c>PalletId</c> next to its inherited key.
/// </summary>
public sealed class ModelFromEntityFrameworkTests(ODataApp app)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Read_only_properties_and_keys_follow_the_ef_model_on_every_route()
    {
        var setup = await app.SetupAsync();
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            var parcels = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
            parcels.AddRange(
                new Consignment { Name = "Light", Weight = 2, Pallets = [new Pallet { PalletId = 7, Label = "A" }] },
                new Consignment { Name = "Heavy", Weight = 10, Pallets = [new Pallet { PalletId = 8, Label = "B" }, new Pallet { PalletId = 9, Label = "C" }] });
            await parcels.SaveChangesAsync(Ct);
        }

        // by convention the computed value is missing and pallets are no entities
        var conventional = app.As(setup.AdminUserId, setup.TenantId);
        (await conventional.GetFromJsonAsync<JsonElement>("/odata/Consignments", Ct)).GetProperty("value")[0].TryGetProperty("DoubleWeight", out _).ShouldBeFalse();
        (await conventional.GetAsync("/odata/Consignments?$expand=Pallets", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var efApp = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:ModelFromEntityFramework"] = "true" });
        var client = efApp.GetTestClient().AsUser(setup.AdminUserId, setup.TenantId);
        var heavy = (await client.GetFromJsonAsync<JsonElement>("/odata/Consignments?$filter=DoubleWeight gt 10&$orderby=DoubleWeight desc&$select=Name,DoubleWeight&$expand=Pallets($orderby=PalletId)", Ct))
            .GetProperty("value").EnumerateArray().Single();
        heavy.GetProperty("Name").GetString().ShouldBe("Heavy");
        heavy.GetProperty("DoubleWeight").GetInt32().ShouldBe(20);
        heavy.GetProperty("Pallets").EnumerateArray().Select(p => p.GetProperty("PalletId").GetInt32()).ShouldBe([8, 9]);
        heavy.GetProperty("Pallets")[0].TryGetProperty("Id", out _).ShouldBeTrue();
        var pallet = (Microsoft.OData.Edm.IEdmEntityType)efApp.Services.GetRequiredService<Microsoft.OData.Edm.IEdmModel>().FindDeclaredType(typeof(Pallet).FullName);
        pallet.DeclaredKey.Select(k => k.Name).ShouldBe(["Id"]);

        var rt = (await client.GetFromJsonAsync<JsonElement>("/rt/odata/Consignments?$filter=pallets/any(p: p/palletId eq 7)&$select=doubleWeight", Ct)).GetProperty("value");
        rt.EnumerateArray().Single().GetProperty("doubleWeight").GetInt32().ShouldBe(4);
    }
}
