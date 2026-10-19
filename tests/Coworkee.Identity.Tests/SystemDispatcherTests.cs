using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Groups;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Tests;

public sealed class SystemDispatcherTests(IdentityApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_system_runs_commands_with_exactly_the_given_permissions_whatever_the_caller_may_do()
    {
        var member = Guid.CreateVersion7();
        using var caller = CurrentUserScope.Begin(new ImpersonatedUser(member, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var system = scope.ServiceProvider.GetRequiredService<ISystemDispatcher>();

        (await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new CreateGroup(new GroupRequest("Direct", null)), Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await system.SendAsync(new CreateGroup(new GroupRequest("Viewers", null)), [IdentityPermissions.Groups.View], Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        var created = await system.SendAsync(new CreateGroup(new GroupRequest("Team Blue", null)), [IdentityPermissions.Groups.Manage], Ct);

        created.IsSuccess.ShouldBeTrue();
        CurrentUserScope.Current!.UserId.ShouldBe(member);
        (await app.As(_setup.AdminUserId, _setup.TenantId).GetStringAsync("/api/v1/identity/groups", Ct)).ShouldContain("Team Blue");
    }
}
