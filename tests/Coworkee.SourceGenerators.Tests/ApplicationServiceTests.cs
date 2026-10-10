using Coworkee.AspNetCore;
using Coworkee.AspNetCore.ApplicationServices;
using Coworkee.Client.Blazor;
using Coworkee.Client.Blazor.Api;
using Coworkee.SourceGenerators.Tests.Sample;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.SourceGenerators.Tests;

/// <summary>The sample service of this assembly: generated endpoints on a test server, called through the generated proxy.</summary>
public sealed class ApplicationServiceTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private ServiceProvider _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ITodoAppService Todos => _client.GetRequiredService<ITodoAppService>();

    private TestPermissions Permissions => _app.Services.GetRequiredService<TestPermissions>();

    public async ValueTask InitializeAsync() => (_app, _client) = await StartAsync(null);

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Proxy_calls_the_service_through_http()
    {
        var created = await Todos.CreateAsync(new SaveTodo("Write docs", false), Ct);
        await Todos.CreateAsync(new SaveTodo("Ship it", true), Ct);

        (await Todos.GetAsync(created.Id, Ct)).ShouldBe(created);
        (await Todos.UpdateAsync(created.Id, new SaveTodo("Write the docs", true), Ct)).Title.ShouldBe("Write the docs");
        (await Todos.GetListAsync(new TodoFilter(null, true, DateTimeOffset.UtcNow), Ct)).Select(t => t.Title).ShouldBe(["Ship it", "Write the docs"]);
        (await Todos.GetListAsync(new TodoFilter("docs", null, null), Ct)).Count.ShouldBe(1);
        (await Todos.CountAsync(null, [], Ct)).ShouldBe(2);
        (await Todos.CountAsync("Ship", [created.Id], Ct)).ShouldBe(0);
        (await Todos.ArchiveAsync(created.Id)).Archived.ShouldBeTrue();
        (await Todos.RenameAsync("Ship it", "Ship it now / today", Ct)).Title.ShouldBe("Ship it now / today");
    }

    [Fact]
    public void Endpoints_follow_the_conventions()
    {
        var routes = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.Contains("todos", StringComparison.Ordinal))
            .Select(e => $"{e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods.Single()} {e.RoutePattern.RawText}");

        routes.ShouldBe(
        [
            "GET /api/v1/todos/{id:guid}",
            "GET /api/v1/todos/",
            "GET /api/v1/todos/count",
            "POST /api/v1/todos/",
            "PUT /api/v1/todos/{id:guid}",
            "DELETE /api/v1/todos/{id:guid}",
            "POST /api/v1/todos/{id:guid}/archive",
            "PUT /api/v1/todos/by-title/{title}",
        ], ignoreOrder: true);
    }

    [Fact]
    public async Task Validation_failures_reach_the_client_with_their_fields()
    {
        var failure = await Should.ThrowAsync<ApiException>(() => Todos.CreateAsync(new SaveTodo("", false), Ct));

        failure.Status.ShouldBe(400);
        failure.Errors!.ShouldContainKey("Title");
    }

    [Fact]
    public async Task Thrown_errors_become_problem_details()
    {
        var failure = await Should.ThrowAsync<ApiException>(() => Todos.GetAsync(Guid.NewGuid(), Ct));

        failure.Status.ShouldBe(404);
        failure.Code.ShouldBe("todo.not_found");
    }

    [Fact]
    public async Task Required_permissions_are_checked()
    {
        var todo = await Todos.CreateAsync(new SaveTodo("Secret", false), Ct);

        (await Should.ThrowAsync<ApiException>(() => Todos.DeleteAsync(todo.Id, Ct))).Status.ShouldBe(403);
        Permissions.Granted.Add(TodoStore.DeletePermission);
        await Todos.DeleteAsync(todo.Id, Ct);
        (await Todos.CountAsync(null, [], Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Ignored_methods_stay_off_the_api() =>
        await Should.ThrowAsync<NotSupportedException>(() => Todos.LocalOnlyAsync());

    [Fact]
    public async Task OpenApi_describes_the_service()
    {
        var document = await _app.GetTestClient().GetStringAsync("/openapi/v1.json", Ct);

        document.ShouldContain("\"/api/v1/todos/{id}\"");
        document.ShouldContain("\"Todos\"");
        document.ShouldContain("\"SaveTodo\"");
    }

    [Fact]
    public async Task Options_override_a_single_service()
    {
        var (app, client) = await StartAsync(options => options.Configure<ITodoAppService>(d => d.Route = "/api/v1/tasks"));
        await using var _ = app;
        await using var __ = client;

        var routes = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText).ToList();

        routes.ShouldContain("/api/v1/tasks/{id:guid}");
        routes.ShouldNotContain("/api/v1/todos/{id:guid}");
    }

    private static async Task<(WebApplication App, ServiceProvider Client)> StartAsync(Action<ApplicationServiceOptions>? configure)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.AddCoworkee<TodoModule>();
        builder.Services.Configure<ApplicationServiceOptions>(options => configure?.Invoke(options));
        var app = builder.Build();
        TodoModule.UseTestUser(app);
        app.UseCoworkee();
        await app.StartAsync(Ct);

        var services = new ServiceCollection();
        services.AddCoworkeeClient(new Uri("http://localhost/"), options => options.ServiceProxies.ConfigureAll(proxy => proxy.HttpClient.Add(client => client
            .ConfigurePrimaryHttpMessageHandler(() => app.GetTestServer().CreateHandler())
            .ConfigureHttpClient(http => http.DefaultRequestHeaders.Add(TodoModule.UserHeader, "alice")))));
        return (app, services.BuildServiceProvider());
    }
}
