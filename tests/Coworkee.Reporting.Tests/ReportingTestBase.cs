using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Tests;

public abstract class ReportingTestBase(ReportingApp app) : IAsyncLifetime
{
    protected ReportingApp App => app;

    protected SetupResultDto Setup { get; private set; } = null!;

    protected List<Sale> Sales { get; } = [];

    protected HttpClient Admin => app.As(Setup.AdminUserId, Setup.TenantId);

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The rows a user of the tenant may see: no other tenant, no hidden region.</summary>
    protected IEnumerable<Sale> Visible => Sales.Where(s => s.TenantId == Setup.TenantId && s.Region != "Hidden");

    public virtual async ValueTask InitializeAsync()
    {
        Setup = await app.SetupAsync();
        await SeedAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    protected async Task<ReportQueryResult> QueryAsync(ReportQueryRequest request, HttpClient? client = null)
    {
        var response = await (client ?? Admin).PostAsJsonAsync("/api/v1/reports/query", request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReportQueryResult>(Ct))!;
    }

    protected async Task<UserDto> CreateUserAsync(string email, params string[] permissions)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct);
        response.EnsureSuccessStatusCode();
        var user = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!;
        if (permissions.Length > 0)
        {
            await AssignRoleAsync(user.Id, await CreateRoleAsync($"Role {email}", permissions));
        }

        return user;
    }

    protected async Task<Guid> CreateRoleAsync(string name, params string[] permissions)
    {
        var role = await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        return role;
    }

    protected async Task AssignRoleAsync(Guid userId, params Guid[] roles) =>
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{userId}/roles", new IdListRequest(roles), Ct)).EnsureSuccessStatusCode();

    private async Task SeedAsync()
    {
        var random = new Random(42);
        var shoppers = Enumerable.Range(1, 7).Select(i => new Shopper { Name = $"Shopper {i}", TenantId = Setup.TenantId }).ToList();
        string[] regions = ["North", "South", "East", "West", "Hidden"];
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 600; i++)
        {
            var soldAt = start.AddMinutes(random.Next(0, 546 * 24 * 60));
            Sales.Add(new Sale
            {
                Region = regions[random.Next(regions.Length)],
                Status = (SaleStatus)random.Next(3),
                Amount = Math.Round((decimal)(random.NextDouble() * 1000), 2),
                Quantity = random.Next(1, 20),
                SoldAt = soldAt,
                ShippedOn = random.Next(4) == 0 ? null : DateOnly.FromDateTime(soldAt.UtcDateTime).AddDays(random.Next(1, 9)),
                ShopperId = shoppers[random.Next(shoppers.Count)].Id,
                Secret = "s",
                TenantId = i % 20 == 0 ? Guid.NewGuid() : Setup.TenantId,
            });
        }

        foreach (var sale in Sales)
        {
            sale.Shopper = shoppers.Single(s => s.Id == sale.ShopperId);
        }

        await app.InDbAsync(null, async db =>
        {
            db.AddRange(shoppers);
            db.AddRange(Sales.Select(s => new Sale
            {
                Region = s.Region, Status = s.Status, Amount = s.Amount, Quantity = s.Quantity, SoldAt = s.SoldAt, ShippedOn = s.ShippedOn,
                ShopperId = s.ShopperId, Secret = s.Secret, TenantId = s.TenantId,
            }));
            await db.SaveChangesAsync(Ct);
        });
    }
}
