using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Core;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Coworkee.AspNetCore.Tests;

public sealed class HttpPipelineTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddCoworkee<TestWebModule>();
        _app = builder.Build();
        _app.UseCoworkee();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Success_returns_value()
    {
        var response = await _client.GetAsync("/value", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<int>(TestContext.Current.CancellationToken)).ShouldBe(42);
    }

    [Fact]
    public async Task Plain_success_returns_no_content() =>
        (await _client.PostAsync("/void", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

    [Theory]
    [InlineData("/missing", HttpStatusCode.NotFound, "thing.missing")]
    [InlineData("/conflict", HttpStatusCode.Conflict, "thing.conflict")]
    public async Task Failures_return_problem_with_code(string path, HttpStatusCode status, string code)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(status);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Validation_failure_returns_400_with_errors()
    {
        var response = await _client.GetAsync("/validate", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("errors").TryGetProperty(nameof(Named.Name), out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Validation_exception_returns_400() =>
        (await _client.GetAsync("/throw-validation", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Concurrency_conflict_returns_409() =>
        (await _client.GetAsync("/throw-concurrency", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

    [Fact]
    public async Task OpenApi_document_lists_endpoints()
    {
        var document = await _client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        document.ShouldContain("\"/value\"");
    }

    public sealed record Named(string Name) : IQuery<Result<string>>;

    internal sealed class NamedValidator : AbstractValidator<Named>
    {
        public NamedValidator() => RuleFor(n => n.Name).NotEmpty();
    }

    internal sealed class NamedHandler : IHandler<Named, Result<string>>
    {
        public Task<Result<string>> HandleAsync(Named request, CancellationToken cancellationToken) => Task.FromResult<Result<string>>(request.Name);
    }

    [DependsOn(typeof(CoworkeeApplicationModule))]
    private sealed class TestWebModule : CoworkeeModule, IWebModule
    {
        public override void ConfigureServices(ModuleServiceContext context) => context.Services.AddMessagingFromAssembly(typeof(HttpPipelineTests).Assembly);

        public void ConfigureApplication(WebApplication app)
        {
            app.MapGet("/value", () => Task.FromResult<Result<int>>(42).ToHttpResult());
            app.MapPost("/void", () => Task.FromResult(Result.Success()).ToHttpResult());
            app.MapGet("/missing", () => Task.FromResult<Result<int>>(Error.NotFound("thing.missing", "Missing")).ToHttpResult());
            app.MapGet("/conflict", () => Task.FromResult(Result.Failure(Error.Conflict("thing.conflict", "Conflict"))).ToHttpResult());
            app.MapGet("/validate", (IDispatcher dispatcher, CancellationToken ct) => dispatcher.SendAsync(new Named(""), ct).ToHttpResult());
            app.MapGet("/throw-validation", IResult () => throw new ValidationException("bad"));
            app.MapGet("/throw-concurrency", IResult () => throw new ConcurrencyConflictException("changed", new InvalidOperationException()));
        }
    }
}
