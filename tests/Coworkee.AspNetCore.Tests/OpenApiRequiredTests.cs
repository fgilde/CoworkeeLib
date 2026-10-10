using System.Text.Json;
using Coworkee.Core.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Coworkee.AspNetCore.Tests;

public sealed class OpenApiRequiredTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Properties_that_can_never_be_null_are_required_in_the_document()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.AddCoworkee<ThingModule>();
        await using var app = builder.Build();
        app.UseCoworkee();
        await app.StartAsync(Ct);

        using var document = JsonDocument.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", Ct));
        var required = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(nameof(ThingDto))
            .GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();

        required.ShouldBe(["id", "name", "rate"], ignoreOrder: true);
    }

    public sealed class ThingDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public decimal Rate { get; set; }
        public int? Count { get; set; }
    }

    private sealed class ThingModule : CoworkeeModule, IWebModule
    {
        public void ConfigureApplication(WebApplication app) => app.MapGet("/things", () => new ThingDto());
    }
}
