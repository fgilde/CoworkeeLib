using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Permissions;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Tests;

/// <summary>Requests that wait on the same cold permission load get their answer, also when the request that started the load ends first.</summary>
public sealed class PermissionLoadTests(IdentityApp app) : IAsyncLifetime
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
    public async Task A_caller_that_aborts_a_shared_load_leaves_the_others_their_permissions()
    {
        await app.App.Services.GetRequiredService<PermissionCache>().InvalidateAsync(Ct);
        var gate = CommandGate.Arm(_setup.AdminUserId);
        using var aborted = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        // the first request starts the load and is held in it; the second joins the same load
        var first = Task.Run(() => GrantedAsync(aborted.Token), Ct);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var second = GrantedAsync(Ct);

        // the browser aborts the first request: it ends, its services are disposed
        await aborted.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(first);
        gate.Released.SetResult();

        (await second.WaitAsync(TimeSpan.FromSeconds(30), Ct)).ShouldContain(IdentityPermissions.Users.View);
    }

    private async Task<IReadOnlyCollection<string>> GrantedAsync(CancellationToken cancellationToken)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissionChecker>().GetGrantedAsync(cancellationToken);
    }
}
