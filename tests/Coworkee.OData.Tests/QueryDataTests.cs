using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.OData.Transfer;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

public sealed class QueryDataTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(
                new Gadget { Name = "Drill", Category = "Tools", Price = 99, SerialCode = "S-1", TenantId = _setup.TenantId },
                new Gadget { Name = "Hammer", Category = "Tools", Price = 19, TenantId = _setup.TenantId },
                new Gadget { Name = "Vault", Category = "Secret", Price = 1000, TenantId = _setup.TenantId });
            return await db.SaveChangesAsync();
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Lists_visible_rows_without_hidden_properties_and_reports_bad_input()
    {
        var page = await SendAsync(new QueryDataQuery("gadgets", "Price gt 10", "Price desc", Top: 1));

        page.Value.Total.ShouldBe(2);
        page.Value.Rows.Single()["Name"].ShouldBe("Drill");
        page.Value.Rows.Single().Keys.ShouldNotContain("SerialCode");
        (await SendAsync(new QueryDataQuery("Gadgets", "Nope eq 1"))).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await SendAsync(new QueryDataQuery("Unknown"))).Error!.Message.ShouldContain("Gadgets");
    }

    private async Task<Result<DataPage>> SendAsync(QueryDataQuery query)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(query, TestContext.Current.CancellationToken);
    }
}
