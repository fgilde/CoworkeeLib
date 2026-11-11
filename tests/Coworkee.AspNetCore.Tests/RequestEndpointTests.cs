using System.Net;
using System.Net.Http.Json;
using Coworkee.Application;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AspNetCore.Tests;

/// <summary>Requests with a route attribute become endpoints without MapGet; controllers in module assemblies are found.</summary>
public sealed class RequestEndpointTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.AddCoworkee<GreetingModule>();
        _app = builder.Build();
        _app.UseCoworkee();
        await _app.StartAsync(Ct);
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Get_binds_route_values_to_the_request() =>
        (await _client.GetFromJsonAsync<string>("/api/v1/greetings/bob", Ct)).ShouldBe("Hello bob");

    [Fact]
    public async Task Post_without_route_values_reads_the_request_from_the_body()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/greetings", new CreateGreeting("bob", 3), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<int>(Ct)).ShouldBe(3);
    }

    [Fact]
    public async Task Route_values_and_a_complex_property_from_the_body_bind_together()
    {
        var id = Guid.NewGuid();

        var response = await _client.PostAsJsonAsync($"/api/v1/greetings/{id}/rename", new RenameBody("alice"), Ct);

        (await response.Content.ReadFromJsonAsync<string>(Ct)).ShouldBe($"{id}:alice");
    }

    [Fact]
    public async Task A_plain_result_returns_no_content() =>
        (await _client.DeleteAsync($"/api/v1/greetings/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

    [Fact]
    public async Task A_value_that_is_no_result_returns_ok() =>
        (await _client.GetFromJsonAsync<int>("/api/v1/greetings/count", Ct)).ShouldBe(7);

    [Fact]
    public async Task The_pipeline_validates_the_request() =>
        (await _client.PostAsJsonAsync("/api/v1/greetings", new CreateGreeting("", 1), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public void Endpoints_require_authorization_unless_they_allow_anonymous()
    {
        Endpoint("/api/v1/secret").Metadata.GetMetadata<IAuthorizeData>().ShouldNotBeNull();
        Endpoint("/api/v1/greetings/{name}").Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull();
    }

    [Fact]
    public async Task OpenApi_describes_the_endpoints_with_a_tag_from_the_route()
    {
        var document = await _client.GetStringAsync("/openapi/v1.json", Ct);

        document.ShouldContain("\"/api/v1/greetings/{name}\"");
        document.ShouldContain("\"Greetings\"");
        document.ShouldContain("\"Secret stuff\"");
    }

    [Fact]
    public async Task Controllers_in_module_assemblies_are_mapped() =>
        (await _client.GetFromJsonAsync<string>("/api/v1/greeting-controller/bob", Ct)).ShouldBe("Hello bob");

    [Theory]
    [InlineData("""{"shade":"Dark","hues":"Red, Blue"}""")]
    [InlineData("""{"shade":1,"hues":5}""")]
    public async Task Enums_in_the_body_bind_from_names_and_numbers_and_come_back_as_names(string json)
    {
        var response = await _client.PostAsync("/api/v1/greetings/paint", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("""{"shade":"Dark","hues":"Red, Blue"}""");
    }

    private RouteEndpoint Endpoint(string pattern) =>
        _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Where(e => e.RoutePattern.RawText == pattern).ShouldHaveSingleItem();

    [Get("/api/v1/greetings/{name}", AllowAnonymous = true)]
    public sealed record Greet(string Name) : IQuery<Result<string>>;

    [Post("/api/v1/greetings", AllowAnonymous = true)]
    public sealed record CreateGreeting(string Name, int Times) : ICommand<Result<int>>;

    public sealed record RenameBody(string Name);

    [Post("/api/v1/greetings/{id:guid}/rename", AllowAnonymous = true)]
    public sealed record RenameGreeting(Guid Id, RenameBody Body) : ICommand<Result<string>>;

    [Delete("/api/v1/greetings/{id:guid}", AllowAnonymous = true)]
    public sealed record DeleteGreeting(Guid Id) : ICommand<Result>;

    [Get("/api/v1/greetings/count", AllowAnonymous = true)]
    public sealed record CountGreetings : IQuery<int>;

    public enum Shade
    {
        Light,
        Dark,
    }

    [Flags]
    public enum Hues
    {
        None = 0,
        Red = 1,
        Green = 2,
        Blue = 4,
    }

    [Post("/api/v1/greetings/paint", AllowAnonymous = true)]
    public sealed record Paint(Shade Shade, Hues Hues) : ICommand<Result<Paint>>;

    [Get("/api/v1/secret", Tag = "Secret stuff")]
    public sealed record GetSecret : IQuery<Result<string>>;

    internal sealed class CreateGreetingValidator : AbstractValidator<CreateGreeting>
    {
        public CreateGreetingValidator() => RuleFor(g => g.Name).NotEmpty();
    }

    internal sealed class GreetingHandlers :
        IHandler<Greet, Result<string>>,
        IHandler<CreateGreeting, Result<int>>,
        IHandler<RenameGreeting, Result<string>>,
        IHandler<DeleteGreeting, Result>,
        IHandler<CountGreetings, int>,
        IHandler<GetSecret, Result<string>>,
        IHandler<Paint, Result<Paint>>
    {
        public Task<Result<Paint>> HandleAsync(Paint request, CancellationToken cancellationToken) => Task.FromResult<Result<Paint>>(request);

        public Task<Result<string>> HandleAsync(Greet request, CancellationToken cancellationToken) => Task.FromResult<Result<string>>($"Hello {request.Name}");

        public Task<Result<int>> HandleAsync(CreateGreeting request, CancellationToken cancellationToken) => Task.FromResult<Result<int>>(request.Times);

        public Task<Result<string>> HandleAsync(RenameGreeting request, CancellationToken cancellationToken) =>
            Task.FromResult<Result<string>>($"{request.Id}:{request.Body.Name}");

        public Task<Result> HandleAsync(DeleteGreeting request, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public Task<int> HandleAsync(CountGreetings request, CancellationToken cancellationToken) => Task.FromResult(7);

        public Task<Result<string>> HandleAsync(GetSecret request, CancellationToken cancellationToken) => Task.FromResult<Result<string>>("secret");
    }

    [DependsOn(typeof(CoworkeeApplicationModule))]
    private sealed class GreetingModule : CoworkeeModule
    {
        public override void ConfigureServices(ModuleServiceContext context) => context.Services.AddMessagingFromAssembly(typeof(RequestEndpointTests).Assembly);
    }
}

[ApiController]
[Route("api/v1/greeting-controller")]
public sealed class GreetingController : ControllerBase
{
    [HttpGet("{name}")]
    public Task<Results<Ok<string>, ProblemHttpResult, ValidationProblem>> Get(string name, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new RequestEndpointTests.Greet(name), cancellationToken).ToHttpResult();
}
