using System.Data.Common;
using Coworkee.Cli.Spec;
using Coworkee.Cli.Spec.Import;
using Microsoft.Data.Sqlite;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Coworkee.Cli.Tests;

public sealed class DbImportTests
{
    public const string Schema = """
        create table customers (id integer primary key, name varchar(120) not null unique, email varchar(200), created_at timestamp);
        create table orders (id integer primary key, number varchar(30) not null, customer_id integer not null references customers(id) on delete cascade,
          total numeric(12,2) not null, ordered_on date, status {0}, note text, paid boolean not null);
        create table tags (id integer primary key, label varchar(50) not null);
        create table order_tags (order_id integer not null references orders(id), tag_id integer not null references tags(id), primary key (order_id, tag_id));
        create table attachments (id integer primary key, title varchar(20) not null, data {1});
        create table countries (code char(2) primary key, name varchar(80) not null);
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sqlite_file_becomes_entities_with_references_and_reports_what_it_skips()
    {
        using var folder = new TempFolder();
        var file = Path.Combine(folder.Path, "legacy-shop.db");
        await CreateAsync(new SqliteConnection($"Data Source={file}"), string.Format(Schema, "varchar(20)", "blob"));
        SqliteConnection.ClearAllPools();

        var result = await EntityImporter.ImportAsync(new ImportRequest(ImportSource.Db, file), Ct);

        result.Module.Name.ShouldBe("LegacyShop");
        ShouldBeTheShop(result, PropertyType.String);
        SpecValidator.Validate(FileImportTests.Spec(result)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_chosen_tables_are_imported_and_references_to_others_stay_plain_values()
    {
        using var folder = new TempFolder();
        var file = Path.Combine(folder.Path, "shop.db");
        await CreateAsync(new SqliteConnection($"Data Source={file}"), string.Format(Schema, "varchar(20)", "blob"));
        SqliteConnection.ClearAllPools();

        var result = await EntityImporter.ImportAsync(new ImportRequest(ImportSource.Db, $"Data Source={file}", "Sales", ["orders"]), Ct);

        result.Module.Entities.Select(e => e.Name).ShouldBe(["Order"]);
        var customer = result.Module.Entities[0].Properties.Single(p => p.Name == "CustomerId");
        (customer.Type, customer.Required).ShouldBe((PropertyType.Int, true));
        result.Skipped.ShouldContain(s => s.StartsWith("orders.customer_id: references customers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_postgres_database_brings_its_enum_types_along()
    {
        await using var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await container.StartAsync(Ct);
        await CreateAsync(new NpgsqlConnection(container.GetConnectionString()),
            "create type order_status as enum ('open', 'in_progress', 'shipped');" + string.Format(Schema, "order_status", "bytea"));

        var result = await EntityImporter.ImportAsync(new ImportRequest(ImportSource.Db, container.GetConnectionString(), "Sales"), Ct);

        ShouldBeTheShop(result, PropertyType.Enum);
        result.Enums.ShouldHaveSingleItem().Values.ShouldBe(["Open", "InProgress", "Shipped"]);
        SpecValidator.Validate(FileImportTests.Spec(result)).ShouldBeEmpty();
    }

    [Fact]
    public void Join_tables_without_own_columns_are_reported_as_many_to_many()
    {
        var link = new TableInfo("dbo", "OrderTags");
        link.Columns.AddRange([new ColumnInfo("OrderId", "int", false, null, null, null), new ColumnInfo("TagId", "int", false, null, null, null), new ColumnInfo("TenantId", "uniqueidentifier", true, null, null, null)]);
        link.PrimaryKey.AddRange(["OrderId", "TagId"]);
        link.ForeignKeys.AddRange([new ForeignKeyInfo("FK_1", "OrderId", "dbo", "Orders", "CASCADE"), new ForeignKeyInfo("FK_2", "TagId", "dbo", "Tags", "NO ACTION")]);

        var result = DbImporter.Map([link], new Dictionary<string, List<string>>(), "Sales");

        result.Module.Entities.ShouldBeEmpty();
        result.Skipped.ShouldHaveSingleItem().ShouldContain("many-to-many between Orders and Tags");
    }

    [Theory]
    [InlineData("C:/data/shop.db", null, "sqlite")]
    [InlineData("Host=localhost;Database=shop;Username=app", null, "postgres")]
    [InlineData("Data Source=shop.db", null, "sqlite")]
    [InlineData("Server=.;Database=Shop;Trusted_Connection=True", null, "sqlServer")]
    [InlineData("Data Source=db;Initial Catalog=Shop", null, "sqlServer")]
    [InlineData("Server=pg;Database=shop", "postgres", "postgres")]
    public void The_provider_is_guessed_from_the_connection_string(string source, string? provider, string expected) =>
        DbSchema.Provider(source, provider).ShouldBe(expected);

    private static void ShouldBeTheShop(ImportResult result, PropertyType status)
    {
        result.Module.Entities.Select(e => e.Name).ShouldBe(["Attachment", "Country", "Customer", "Order", "Tag"]);
        var customer = result.Module.Entities.Single(e => e.Name == "Customer").Properties;
        customer.Select(p => (p.Name, p.Type, p.Required, p.MaxLength, p.Unique)).ShouldBe(
            [("Name", PropertyType.String, true, (int?)120, true), ("Email", PropertyType.Email, false, 200, false)]);
        var order = result.Module.Entities.Single(e => e.Name == "Order").Properties;
        order.Select(p => p.Name).ShouldBe(["Number", "Customer", "Total", "OrderedOn", "Status", "Note", "Paid"]);
        var reference = order.Single(p => p.Name == "Customer");
        (reference.Type, reference.Reference, reference.Required, reference.OnDelete).ShouldBe((PropertyType.Reference, "Customer", true, DeleteBehavior.Cascade));
        var total = order.Single(p => p.Name == "Total");
        (total.Type, total.Precision, total.Scale).ShouldBe((PropertyType.Decimal, 12, 2));
        order.Single(p => p.Name == "OrderedOn").Type.ShouldBe(PropertyType.DateOnly);
        order.Single(p => p.Name == "Status").Type.ShouldBe(status);
        order.Single(p => p.Name == "Paid").Type.ShouldBe(PropertyType.Bool);
        var code = result.Module.Entities.Single(e => e.Name == "Country").Properties[0];
        (code.Name, code.Required, code.Unique).ShouldBe(("Code", true, true));
        result.Module.Entities.Single(e => e.Name == "Country").Plural.ShouldBeNull();
        result.Skipped.ShouldContain(s => s.Contains("many-to-many between orders and tags", StringComparison.Ordinal));
        result.Skipped.ShouldContain(s => s.StartsWith("attachments.data: type", StringComparison.Ordinal));
        result.Skipped.ShouldContain(s => s.StartsWith("customers: id, created_at left out", StringComparison.Ordinal));
        result.Skipped.ShouldContain(s => s.StartsWith("countries.code: part of the primary key", StringComparison.Ordinal));
    }

    private static async Task CreateAsync(DbConnection connection, string sql)
    {
        await using (connection)
        {
            await connection.OpenAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(Ct);
        }
    }
}
