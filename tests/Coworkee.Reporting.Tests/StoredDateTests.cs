using Coworkee.Reporting.Querying;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Coworkee.Reporting.Tests;

/// <summary>SQLite date buckets read a DateTimeOffset from its stored number only where the column's context maps the function they need.</summary>
public sealed class StoredDateTests(ReportingApp app) : ReportingTestBase(app)
{
    [Fact]
    public async Task An_external_context_with_the_binary_converter_is_refused()
    {
        var soldAt = typeof(Sale).GetProperty(nameof(Sale.SoldAt))!;
        await App.InDbAsync(Setup.TenantId, db =>
        {
            // a Coworkee DbContext stores DateTimeOffset as a number on SQLite only
            ReportQueries.StoredAsNumber(db.Model, typeof(Sale), [soldAt]).ShouldBe(App.Provider == Coworkee.Contracts.Configuration.DatabaseProviders.Sqlite);
            return Task.CompletedTask;
        });

        await using var external = new External();
        ReportQueries.StoredAsNumber(external.Model, typeof(Sale), [soldAt]).ShouldBeFalse();
    }

    private sealed class External : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=:memory:");

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Sale>().Property(s => s.SoldAt).HasConversion<DateTimeOffsetToBinaryConverter>();
    }
}
