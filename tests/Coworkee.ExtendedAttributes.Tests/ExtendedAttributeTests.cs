using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.ExtendedAttributes;
using Coworkee.Contracts.Identity;

namespace Coworkee.ExtendedAttributes.Tests;

public sealed class ExtendedAttributeTests(AttributesApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _note;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _note = await app.AddNoteAsync(_setup.TenantId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private string Url => $"/api/v1/attributes/Notes/{_note}";

    [Fact]
    public async Task Saving_replaces_the_list_and_keeps_only_the_value_of_the_type()
    {
        var first = await SaveAsync(
            new ExtendedAttributeDto { Key = "Pages", Type = ExtendedAttributeType.Decimal, Decimal = 12, Text = "ignored" },
            new ExtendedAttributeDto { Key = "Meta", Type = ExtendedAttributeType.Json, Json = """{"a":1}""", Group = "Tech" });
        first.Select(a => a.Key).ShouldBe(["Pages", "Meta"], ignoreOrder: true);
        first.Single(a => a.Key == "Pages").Text.ShouldBeNull();

        var second = await SaveAsync(new ExtendedAttributeDto { Key = "pages", Type = ExtendedAttributeType.Text, Text = "twelve" });

        second.Single().Id.ShouldBe(first.Single(a => a.Key == "Pages").Id);
        (second.Single().Type, second.Single().Text, second.Single().Decimal).ShouldBe((ExtendedAttributeType.Text, "twelve", (decimal?)null));
        (await Admin.GetFromJsonAsync<List<ExtendedAttributeDto>>(Url, Ct))!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Bad_input_unknown_entities_and_missing_permissions_are_rejected()
    {
        (await PutAsync(new ExtendedAttributeDto { Key = "A" }, new ExtendedAttributeDto { Key = "a" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PutAsync(new ExtendedAttributeDto { Key = "Meta", Type = ExtendedAttributeType.Json, Json = "{nope" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.GetAsync($"/api/v1/attributes/Notes/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.GetAsync($"/api/v1/attributes/Unknown/{_note}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var foreign = await app.AddNoteAsync(Guid.NewGuid());
        (await Admin.GetAsync($"/api/v1/attributes/Notes/{foreign}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("viewer@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct);
        (await app.As(user!.Id, _setup.TenantId).GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<List<ExtendedAttributeDto>> SaveAsync(params ExtendedAttributeDto[] attributes)
    {
        var response = await PutAsync(attributes);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<ExtendedAttributeDto>>(Ct))!;
    }

    private Task<HttpResponseMessage> PutAsync(params ExtendedAttributeDto[] attributes) =>
        Admin.PutAsJsonAsync(Url, new SaveExtendedAttributesRequest(attributes), Ct);
}
