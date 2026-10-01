using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Application.Tests;

public sealed class AuthorizationTests
{
    private readonly IPermissionChecker _checker = Substitute.For<IPermissionChecker>();
    private readonly FakeUser _user = new();

    [Fact]
    public async Task Requests_without_requirements_pass_anonymously()
    {
        _user.UserId = null;

        (await Dispatcher().SendAsync(new Open(), Ct)).ShouldBe("open");
    }

    [Fact]
    public async Task Anonymous_user_is_unauthorized()
    {
        _user.UserId = null;

        var result = await Dispatcher().SendAsync(new Guarded(), Ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Unauthorized);
    }

    [Fact]
    public async Task Missing_permission_is_forbidden()
    {
        var result = await Dispatcher().SendAsync(new Guarded(), Ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Missing_permission_on_plain_request_throws()
    {
        await Should.ThrowAsync<ForbiddenException>(() => Dispatcher().SendAsync(new GuardedPlain(), Ct));
    }

    [Fact]
    public async Task Granted_permission_runs_the_handler()
    {
        _checker.IsGrantedAsync("Demo.Read", Arg.Any<CancellationToken>()).Returns(true);

        (await Dispatcher().SendAsync(new Guarded(), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Resource_requests_are_checked_against_the_resource()
    {
        var id = Guid.CreateVersion7();
        _checker.IsGrantedAsync("Folder.Edit", "Folder", id, Arg.Any<CancellationToken>()).Returns(true);

        (await Dispatcher().SendAsync(new OnResource(id), Ct)).IsSuccess.ShouldBeTrue();
        (await Dispatcher().SendAsync(new OnResource(Guid.CreateVersion7()), Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public void Definitions_expand_implications_transitively()
    {
        var manager = Manager(ctx => ctx.Group("Demo", "Demo")
            .Add("Demo.Read", "Read")
            .Add("Demo.Write", "Write", "Demo.Read")
            .Add("Demo.Admin", "Admin", "Demo.Write"));

        manager.Expand(["Demo.Admin"]).ShouldBe(["Demo.Admin", "Demo.Write", "Demo.Read"], ignoreOrder: true);
        manager.Exists("Demo.Write").ShouldBeTrue();
        manager.Exists("Demo.Unknown").ShouldBeFalse();
    }

    [Fact]
    public void Duplicate_definitions_are_rejected() =>
        Should.Throw<InvalidOperationException>(() => Manager(ctx => ctx.Group("A", "A").Add("X", "X").Add("X", "X")).All);

    [Fact]
    public void Unknown_implications_are_rejected() =>
        Should.Throw<InvalidOperationException>(() => Manager(ctx => ctx.Group("A", "A").Add("X", "X", "Missing")).All);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IDispatcher Dispatcher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(_user);
        services.AddSingleton(_checker);
        services.AddCoworkeeModules<TestModule>(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IDispatcher>();
    }

    private static IPermissionDefinitionManager Manager(Action<PermissionDefinitionContext> define)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPermissionDefinitionContributor>(new InlineContributor(define));
        services.AddCoworkeeModules<CoworkeeApplicationModule>(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider().GetRequiredService<IPermissionDefinitionManager>();
    }

    private sealed class InlineContributor(Action<PermissionDefinitionContext> define) : IPermissionDefinitionContributor
    {
        public void Define(PermissionDefinitionContext context) => define(context);
    }

    private sealed class FakeUser : ICurrentUser
    {
        public Guid? UserId { get; set; } = Guid.CreateVersion7();

        public Guid? TenantId { get; set; } = Guid.CreateVersion7();

        public bool IsAuthenticated => UserId is not null;

        public IReadOnlyCollection<string> Roles => [];
    }

    [DependsOn(typeof(CoworkeeApplicationModule))]
    private sealed class TestModule : CoworkeeModule
    {
        public override void ConfigureServices(ModuleServiceContext context) =>
            context.Services.AddMessagingFromAssembly(typeof(AuthorizationTests).Assembly);
    }

    public sealed record Open : IQuery<string>;

    [RequiresPermission("Demo.Read")]
    public sealed record Guarded : IQuery<Result>;

    [RequiresPermission("Demo.Read")]
    public sealed record GuardedPlain : IQuery<string>;

    public sealed record OnResource(Guid ResourceId) : ICommand<Result>, IResourceRequest
    {
        public string ResourceType => "Folder";

        public string RequiredPermission => "Folder.Edit";
    }

    internal sealed class OpenHandler : IHandler<Open, string>
    {
        public Task<string> HandleAsync(Open request, CancellationToken cancellationToken) => Task.FromResult("open");
    }

    internal sealed class GuardedHandler : IHandler<Guarded, Result>
    {
        public Task<Result> HandleAsync(Guarded request, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    internal sealed class GuardedPlainHandler : IHandler<GuardedPlain, string>
    {
        public Task<string> HandleAsync(GuardedPlain request, CancellationToken cancellationToken) => Task.FromResult("x");
    }

    internal sealed class OnResourceHandler : IHandler<OnResource, Result>
    {
        public Task<Result> HandleAsync(OnResource request, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }
}
