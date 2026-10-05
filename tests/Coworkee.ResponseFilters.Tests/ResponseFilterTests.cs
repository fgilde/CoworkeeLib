using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Nextended.ResponseFilters;

namespace Coworkee.ResponseFilters.Tests;

public sealed class ResponseFilterTests : IAsyncLifetime
{
    private WebApplication _app = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddCoworkeeResponseFilters([typeof(ResponseFilterTests).Assembly]);
        _app = builder.Build();
        var api = _app.MapCoworkeeApi("/api/v1/people");
        api.MapGet("/{name}", (string name) => Task.FromResult(Result<Person>.Success(new Person(name, "secret-token", "4111111111111111"))).ToHttpResult());
        _app.MapGet("/outside", () => new Person("Ada", "secret-token", "4111111111111111"));
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Filters_change_and_drop_values_of_api_results()
    {
        var person = await _app.GetTestClient().GetFromJsonAsync<JsonElement>("/api/v1/people/ada", Ct);

        person.GetProperty("name").GetString().ShouldBe("ada");
        person.TryGetProperty("token", out _).ShouldBeFalse();
        person.GetProperty("card").GetString().ShouldBe("4111********1111");
    }

    [Fact]
    public async Task Endpoints_outside_the_api_groups_are_left_alone() =>
        (await _app.GetTestClient().GetFromJsonAsync<JsonElement>("/outside", Ct)).GetProperty("token").GetString().ShouldBe("secret-token");

    public sealed record Person(string Name, string Token, string Card);

    public sealed class PersonFilter : ResponseFilter<Person>
    {
        public PersonFilter()
        {
            Remove(p => p.Token).Always();
            Mask(p => p.Card).KeepFirst(4).KeepLast(4).Always();
        }
    }
}
