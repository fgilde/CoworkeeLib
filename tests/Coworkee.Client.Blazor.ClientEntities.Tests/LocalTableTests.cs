namespace Coworkee.Client.Blazor.ClientEntities.Tests;

public sealed class LocalTableTests
{
    private static readonly Gadget[] Rows =
    [
        new(Guid.CreateVersion7(), "Drill", "Tools", 99.5m, new Brand(Guid.CreateVersion7(), "Acme"), Kind.Power, DateTimeOffset.Parse("2026-03-01T10:00:00Z")),
        new(Guid.CreateVersion7(), "hammer", "Tools", 19m, null, Kind.Hand, DateTimeOffset.Parse("2026-01-15T10:00:00Z")),
        new(Guid.CreateVersion7(), "Lamp", "Light", 35m, new Brand(Guid.CreateVersion7(), "Lumen"), Kind.Power, DateTimeOffset.Parse("2025-12-24T10:00:00Z"), Note: "it's bright"),
    ];

    [Fact]
    public async Task Rows_and_sort_orders_come_back_from_their_columns()
    {
        using var stream = new MemoryStream();
        var written = LocalTable<Gadget>.Of(Rows);
        written.Column("Name").Index().ShouldBeTrue();
        written.Write(stream, DateTimeOffset.Parse("2026-10-10T10:00:00Z"));
        stream.Position = 0;

        var (table, version) = (await LocalTable<Gadget>.ReadAsync(stream, () => Task.CompletedTask))!.Value;

        version.ShouldBe(DateTimeOffset.Parse("2026-10-10T10:00:00Z"));
        table.All().ShouldBe(Rows);
        table.Column("Name").Index().ShouldBeFalse("the sort order came with the columns");
        table.Find(null, "Name").Select(i => table.Row(i).Name).ShouldBe(["Drill", "hammer", "Lamp"]);
    }

    [Theory]
    [InlineData("Category eq 'Tools' and Price gt 20", "Drill")]
    [InlineData("Price ge 19.5 and Price lt 99.5", "Lamp")]
    [InlineData("Brand/Name eq null", "hammer")]
    [InlineData("not (Category eq 'Tools')", "Lamp")]
    [InlineData("Kind eq 'Hand'", "hammer")]
    [InlineData("Name in ('Lamp','Saw')", "Lamp")]
    [InlineData("contains(tolower(Name),'am')", "hammer,Lamp")]
    [InlineData("Price gt 19.5 and length(Name) eq 4", "Lamp")]
    [InlineData("Brand/Name lt 'Lz'", "Drill,Lamp")]
    public void Filters_on_columns_like_the_server(string filter, string expected)
    {
        var table = LocalTable<Gadget>.Of(Rows);

        table.Find(filter, "Name").Select(i => table.Row(i).Name).ShouldBe(expected.Split(','));
    }

    [Fact]
    public void Orders_by_one_or_several_columns_with_nulls_first()
    {
        var table = LocalTable<Gadget>.Of(Rows);

        table.Find(null, "Name desc").Select(i => table.Row(i).Name).ShouldBe(["Lamp", "hammer", "Drill"]);
        table.Find(null, "Brand/Name,Price desc").Select(i => table.Row(i).Name).ShouldBe(["hammer", "Drill", "Lamp"]);
        table.Find(null, "Kind desc,Price").Select(i => table.Row(i).Name).ShouldBe(["Lamp", "Drill", "hammer"]);
    }
}
