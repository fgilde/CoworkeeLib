using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Authorization;
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
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IPermissionChecker, HeaderPermissionChecker>();
        builder.Services.AddControllers().AddApplicationPart(typeof(ResponseFilterTests).Assembly);
        _app = builder.Build();
        _app.MapControllers();
        var api = _app.MapCoworkeeApi("/api/v1/people");
        api.MapGet("/{name}", (string name) => Task.FromResult(Result<Person>.Success(new Person(name, "secret-token", "4111111111111111"))).ToHttpResult());
        api.MapGet("/salary/{name}", (string name) => Task.FromResult(Result<Employee>.Success(new Employee(name, 5000))).ToHttpResult());
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

    [Fact]
    public async Task Controllers_are_filtered_but_deferred_queries_are_left_to_their_serializer()
    {
        (await _app.GetTestClient().GetFromJsonAsync<JsonElement>("/mvc/people/one", Ct)).TryGetProperty("token", out _).ShouldBeFalse();

        var all = await _app.GetTestClient().GetFromJsonAsync<JsonElement>("/mvc/people", Ct);

        all[0].GetProperty("token").GetString().ShouldBe("secret-token");
    }

    [Fact]
    public async Task Permission_rules_apply_only_to_callers_without_the_permission()
    {
        using var granted = new HttpRequestMessage(HttpMethod.Get, "/api/v1/people/salary/ada");
        granted.Headers.Add("X-Permission", "Hr.Salaries");

        (await _app.GetTestClient().GetFromJsonAsync<JsonElement>("/api/v1/people/salary/ada", Ct)).GetProperty("salary").ValueKind.ShouldBe(JsonValueKind.Null);
        (await (await _app.GetTestClient().SendAsync(granted, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("salary").GetInt32().ShouldBe(5000);
    }

    public sealed record Person(string Name, string Token, string Card);

    public sealed record Employee(string Name, int? Salary);

    public sealed class EmployeeFilter : ResponseFilter<Employee>
    {
        public EmployeeFilter() => Nullify(e => e.Salary).UnlessGranted("Hr.Salaries");
    }

    public sealed class PersonFilter : ResponseFilter<Person>
    {
        public PersonFilter()
        {
            Remove(p => p.Token).Always();
            Mask(p => p.Card).KeepFirst(4).KeepLast(4).Always();
        }
    }
}
