using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using System.Net.Http.Json;
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

    [Fact]
    public async Task A_grant_written_in_a_transaction_counts_for_a_check_in_that_transaction_and_is_not_cached()
    {
        var created = await app.As(_setup.AdminUserId, _setup.TenantId).PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("tx@acme.test", "Passw0rd!x", null, null), Ct);
        var user = (await created.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId));
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityTestDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            db.Add(new PermissionGrant { Name = IdentityPermissions.Roles.View, ProviderType = PermissionProviderType.User, ProviderKey = user, TenantId = _setup.TenantId });
            await db.SaveChangesAsync(Ct);

            // the check reads through the transaction: it sees the grant and does not wait on its locks (SQL Server)
            (await scope.ServiceProvider.GetRequiredService<IPermissionChecker>().IsGrantedAsync(IdentityPermissions.Roles.View, Ct)
                .WaitAsync(TimeSpan.FromSeconds(15), Ct)).ShouldBeTrue();
            await transaction.RollbackAsync(Ct);
        }

        // rolled back, and nothing of it was cached
        await using var after = app.App.Services.CreateAsyncScope();
        (await after.ServiceProvider.GetRequiredService<IPermissionChecker>().IsGrantedAsync(IdentityPermissions.Roles.View, Ct)).ShouldBeFalse();
    }

    private async Task<IReadOnlyCollection<string>> GrantedAsync(CancellationToken cancellationToken)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissionChecker>().GetGrantedAsync(cancellationToken);
    }
}
