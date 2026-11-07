using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;

namespace Coworkee.OData.Tests;

// The copy as a SQLite file: the same rows as the OData set (tenant, row filters, hidden properties, permission), in the client's formats.
public sealed class ClientEntitySnapshotTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private readonly Guid _otherTenant = Guid.CreateVersion7();

    private static readonly ClientEntitySnapshotRequest Request = new(
    [
        new ClientEntitySnapshotColumn("Id", ClientEntitySnapshotKinds.Guid, true),
        new ClientEntitySnapshotColumn("Name", ClientEntitySnapshotKinds.Text, true),
        new ClientEntitySnapshotColumn("Price", ClientEntitySnapshotKinds.Real, true),
        new ClientEntitySnapshotColumn("CreatedAt", ClientEntitySnapshotKinds.DateTimeOffset, false),
    ], null);

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(
                new Gadget { Name = "Drill", Category = "Tools", Price = 99.5m, TenantId = _setup.TenantId },
                new Gadget { Name = "Lamp", Category = "Light", Price = 35, TenantId = _setup.TenantId },
                new Gadget { Name = "Vault", Category = "Secret", Price = 1000, TenantId = _setup.TenantId });
            return await db.SaveChangesAsync();
        });
        await app.InDbAsync(_otherTenant, async db =>
        {
            db.Add(new Gadget { Name = "Foreign", Category = "Tools", Price = 1, TenantId = _otherTenant });
            return await db.SaveChangesAsync();
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task The_file_holds_the_rows_of_the_odata_set_in_the_clients_formats()
    {
        var before = DateTimeOffset.UtcNow;
        var response = await Admin.PostAsJsonAsync("/api/v1/client-entities/Gadgets/snapshot", Request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.ShouldContain("gzip");
        var version = DateTimeOffset.Parse(response.Headers.GetValues(ClientEntitySnapshotKinds.VersionHeader).Single(), System.Globalization.CultureInfo.InvariantCulture);
        version.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));

        var path = await SaveAsync(response);
        try
        {
            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync(Ct);
            var rows = new List<(string Id, string Name, double Price, DateTimeOffset CreatedAt)>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, Name, Price, CreatedAt FROM rows ORDER BY Name";
                await using var reader = await command.ExecuteReaderAsync(Ct);
                while (await reader.ReadAsync(Ct))
                {
                    rows.Add((reader.GetString(0), reader.GetString(1), reader.GetDouble(2), ClientEntitySnapshotKinds.FromInstant(reader.GetInt64(3))));
                }
            }

            // own tenant, without the row filtered vault
            rows.Select(r => r.Name).ShouldBe(["Drill", "Lamp"]);
            rows[0].Price.ShouldBe(99.5);
            rows[0].Id.ShouldBe(rows[0].Id.ToUpperInvariant());
            rows[0].CreatedAt.ShouldBeGreaterThan(before.AddMinutes(-5));
            await using (var meta = connection.CreateCommand())
            {
                meta.CommandText = "SELECT version FROM meta";
                ((long)(await meta.ExecuteScalarAsync(Ct))!).ShouldBe(version.UtcTicks);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Hidden_properties_unknown_sets_and_readers_without_the_permission_get_no_file()
    {
        var hidden = Request with { Columns = [.. Request.Columns, new ClientEntitySnapshotColumn("SerialCode", ClientEntitySnapshotKinds.Text, false)] };
        (await Admin.PostAsJsonAsync("/api/v1/client-entities/Gadgets/snapshot", hidden, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/client-entities/Unknown/snapshot", Request, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var email = $"viewer-{Guid.NewGuid():N}@acme.test";
        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);
        (await app.As(user!.Id, _setup.TenantId).PostAsJsonAsync("/api/v1/client-entities/Gadgets/snapshot", Request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_set_above_its_row_limit_gets_no_file()
    {
        await using var small = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:ClientEntities:Default:MaxRows"] = "1" });
        var client = small.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);

        (await client.PostAsJsonAsync("/api/v1/client-entities/Gadgets/snapshot", Request, Ct)).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    private static async Task<string> SaveAsync(HttpResponseMessage response)
    {
        var path = Path.Combine(Path.GetTempPath(), $"coworkee-snapshot-test-{Guid.NewGuid():N}.db");
        await using var gzip = new GZipStream(await response.Content.ReadAsStreamAsync(Ct), CompressionMode.Decompress);
        await using var file = File.Create(path);
        await gzip.CopyToAsync(file, Ct);
        return path;
    }
}
