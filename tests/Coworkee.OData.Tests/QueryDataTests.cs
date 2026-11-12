using System.Net.Http.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Data;
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
        (await SendAsync(new QueryDataQuery("Gadgets", Select: "Name, price"))).Value.Rows[0].Keys.ShouldBe(["Name", "Price"], ignoreOrder: true);
        (await SendAsync(new QueryDataQuery("Gadgets", Select: "Name,SerialCode"))).Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task Lists_the_sets_the_user_may_read_with_their_visible_fields()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();

        var sets = (await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new ListDataSetsQuery(), TestContext.Current.CancellationToken)).Value;

        var gadgets = sets.Single(s => s.Name == "Gadgets");
        gadgets.Fields["Name"].ShouldBe("String");
        gadgets.Fields.Keys.ShouldNotContain("SerialCode");
        // the key from the EF model, Name from IndexedFields; the hidden SerialCode stays out though it is declared
        gadgets.Indexed.ShouldBe(["Id", "Name"], ignoreOrder: true);
        // the key and the index EF gives the foreign key, nothing else
        var pumps = sets.Single(s => s.Name == "Pumps");
        pumps.Indexed.ShouldContain("Id");
        pumps.Indexed.ShouldContain("MakerId");
        pumps.Indexed.ShouldNotContain("Name");
    }

    [Fact]
    public async Task The_client_entity_manifest_names_exactly_the_fields_query_data_returns()
    {
        var manifest = await app.As(_setup.AdminUserId, _setup.TenantId).GetFromJsonAsync<ClientEntityManifest>("/api/v1/client-entities", TestContext.Current.CancellationToken);
        var row = (await SendAsync(new QueryDataQuery("Gadgets", Top: 1))).Value.Rows.Single();

        // the assistant answers a read without select from the browser's copy only when its row type has all of them
        var fields = manifest!.Sets.Single(s => s.EntitySet == "Gadgets").Fields.ShouldNotBeNull();
        fields.ShouldBe(row.Keys, ignoreOrder: true);
        fields.ShouldNotContain("SerialCode");
    }

    private async Task<Result<DataPage>> SendAsync(QueryDataQuery query)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(query, TestContext.Current.CancellationToken);
    }
}
