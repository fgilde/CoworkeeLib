using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.Edm;

namespace Coworkee.OData.Tests;

/// <summary>Hide and HideUnless on members a base class declares: boxes hide them, bags of the same base keep them.</summary>
public sealed class InheritedMemberTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            var tenant = _setup.TenantId;
            db.AddRange(
                new Box { Name = "Box", Note = "fragile", Code = "B1", TenantId = tenant, Stickers = [new Sticker { Text = "Up", TenantId = tenant }] },
                new Bag { Name = "Bag", Note = "light", Code = "G1", TenantId = tenant, Stickers = [new Sticker { Text = "Dry", TenantId = tenant }] });
            return await db.SaveChangesAsync();
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Hidden_members_of_a_base_class_are_hidden_on_their_set_only()
    {
        var model = app.App.Services.GetRequiredService<Microsoft.OData.Edm.IEdmModel>();
        ((Microsoft.OData.Edm.IEdmStructuredType)model.FindDeclaredType(typeof(Box).FullName)).BaseType().ShouldNotBeNull("the EDM shares the base type");
        var box = (await Admin.GetFromJsonAsync<JsonElement>("/odata/Boxes", Ct)).GetProperty("value")[0];
        box.TryGetProperty("Code", out _).ShouldBeFalse();
        box.GetProperty("Note").GetString().ShouldBe("fragile");
        foreach (var query in new[] { "$expand=Stickers", "$filter=Code eq 'B1'", "$select=Code" })
        {
            (await Admin.GetAsync($"/odata/Boxes?{query}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, query);
        }

        var bag = (await Admin.GetFromJsonAsync<JsonElement>("/odata/Bags?$expand=Stickers&$filter=Code eq 'G1'", Ct)).GetProperty("value")[0];
        bag.GetProperty("Code").GetString().ShouldBe("G1");
        bag.GetProperty("Stickers")[0].GetProperty("Text").GetString().ShouldBe("Dry");

        // through a navigation to the base, each row keeps the rules of its own set
        var owners = (await Admin.GetFromJsonAsync<JsonElement>("/odata/Stickers?$expand=Owner&$orderby=Text", Ct)).GetProperty("value");
        owners[0].GetProperty("Owner").GetProperty("Code").GetString().ShouldBe("G1");
        owners[1].GetProperty("Owner").TryGetProperty("Code", out _).ShouldBeFalse();
        (await Admin.GetStringAsync("/api/v1/data/Boxes/export?format=csv", Ct)).ShouldNotContain("Code");
    }

    [Fact]
    public async Task A_navigation_to_the_base_type_applies_the_row_filters_of_the_derived_sets()
    {
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.Add(new Box { Name = "Secret", TenantId = _setup.TenantId, Stickers = [new Sticker { Text = "Hush", TenantId = _setup.TenantId }] });
            return await db.SaveChangesAsync();
        });

        var stickers = (await Admin.GetFromJsonAsync<JsonElement>("/odata/Stickers?$expand=Owner&$orderby=Text", Ct)).GetProperty("value");

        stickers.EnumerateArray().Select(s => s.GetProperty("Owner").ValueKind == JsonValueKind.Null ? null : s.GetProperty("Owner").GetProperty("Name").GetString())
            .ShouldBe(["Bag", null, "Box"]);
        (await Admin.GetFromJsonAsync<JsonElement>("/odata/Stickers?$filter=Owner/Name eq 'Secret'", Ct)).GetProperty("value").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Protected_members_of_a_base_class_need_their_permission_on_their_set_only()
    {
        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"viewer-{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct);
        var role = await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Viewers " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([ODataApp.ViewGadgets]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user!.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        var viewer = app.As(user.Id, _setup.TenantId);

        (await viewer.GetFromJsonAsync<JsonElement>("/odata/Boxes", Ct)).GetProperty("value")[0].TryGetProperty("Note", out _).ShouldBeFalse();
        (await viewer.GetAsync("/odata/Boxes?$filter=Note eq 'fragile'", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.GetFromJsonAsync<JsonElement>("/odata/Bags", Ct)).GetProperty("value")[0].GetProperty("Note").GetString().ShouldBe("light");
    }
}
