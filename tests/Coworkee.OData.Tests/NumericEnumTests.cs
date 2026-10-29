using System.Net.Http.Json;
using System.Text.Json;

namespace Coworkee.OData.Tests;

/// <summary>Enum properties compare with the member's number too (<c>Kind ne 1</c>), as clients that send enums as integers write them.</summary>
public sealed class NumericEnumTests(ODataApp app)
{
    [Theory]
    [InlineData("odata")]
    [InlineData("rt/odata")]
    public async Task An_enum_compares_with_its_number(string route)
    {
        var setup = await app.SetupAsync();
        await app.InDbAsync(setup.TenantId, async db =>
        {
            db.AddRange(
                new Widget { Name = "Anvil", Kind = WidgetKind.Hammer, Color = "Red", TenantId = setup.TenantId },
                new Widget { Name = "Bolt", Kind = WidgetKind.Wrench, Color = "Red", TenantId = setup.TenantId });
            return await db.SaveChangesAsync();
        });
        var client = app.As(setup.AdminUserId, setup.TenantId);

        foreach (var (filter, expected) in new[] { ("Kind eq 1", "Bolt"), ("Kind ne 1", "Anvil"), ("(Kind ne 1)", "Anvil") })
        {
            var response = await client.GetAsync($"/{route}/Widgets?$filter={Uri.EscapeDataString(filter)}", TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            response.IsSuccessStatusCode.ShouldBeTrue($"{filter}: {body}");
            JsonDocument.Parse(body).RootElement.GetProperty("value").EnumerateArray()
                .Select(w => (w.TryGetProperty("Name", out var name) ? name : w.GetProperty("name")).GetString()).ShouldBe([expected], filter);
        }
    }
}
