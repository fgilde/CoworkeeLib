using Coworkee.Client.Blazor.Data;
using MudBlazor;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

public sealed class ODataLinqTests
{
    private static readonly Guid AcmeId = Guid.CreateVersion7();

    private static readonly Gadget[] Rows =
    [
        new(Guid.CreateVersion7(), "Drill", "Tools", 99.5m, new Brand(AcmeId, "Acme"), Kind.Power, DateTimeOffset.Parse("2026-03-01T10:00:00Z")),
        new(Guid.CreateVersion7(), "hammer", "Tools", 19m, null, Kind.Hand, DateTimeOffset.Parse("2026-01-15T10:00:00Z")),
        new(Guid.CreateVersion7(), "Lamp", "Light", 35m, new Brand(Guid.CreateVersion7(), "Lumen"), Kind.Power, DateTimeOffset.Parse("2025-12-24T10:00:00Z"), Note: "it's bright"),
    ];

    [Theory]
    [InlineData("contains(tolower(Name),'ham')", "hammer")]
    [InlineData("Category eq 'Tools' and Price gt 20", "Drill")]
    [InlineData("Price ge 19.5 and Price lt 99.5", "Lamp")]
    [InlineData("Brand/Name eq 'Lumen'", "Lamp")]
    [InlineData("Brand/Name eq null", "hammer")]
    [InlineData("not (Category eq 'Tools')", "Lamp")]
    [InlineData("Kind eq 'Hand'", "hammer")]
    [InlineData("Created ge '2026-01-01T00:00:00Z' and Created lt '2026-02-01T00:00:00Z'", "hammer")]
    [InlineData("Created gt 2026-02-01T00:00:00Z", "Drill")]
    [InlineData("Name in ('Lamp','Saw')", "Lamp")]
    [InlineData("contains(Note,'''s')", "Lamp")]
    [InlineData("startswith(Name,'Dr') or endswith(Name,'mer')", "Drill,hammer")]
    [InlineData("length(Name) eq 4 and year(Created) eq 2025", "Lamp")]
    public void Filters_like_the_server(string filter, string expected) =>
        Rows.Where(ODataLinq.Filter<Gadget>(filter).Compile()).Select(r => r.Name).ShouldBe(expected.Split(','));

    [Fact]
    public void Guids_compare_with_and_without_quotes() =>
        Rows.Count(ODataLinq.Filter<Gadget>($"Brand/Id eq {AcmeId} or Id in ({Rows[1].Id})").Compile()).ShouldBe(2);

    [Fact]
    public void Orders_by_paths_with_nulls_first_and_text_ignoring_case()
    {
        ODataLinq.OrderBy(Rows, "Name").Select(r => r.Name).ShouldBe(["Drill", "hammer", "Lamp"]);
        ODataLinq.OrderBy(Rows, "Brand/Name desc, Price").Select(r => r.Name).ShouldBe(["Lamp", "Drill", "hammer"]);
    }

    [Fact]
    public void Answers_the_filters_of_table_columns()
    {
        var day = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        Matching(new ColumnCondition("Name", typeof(string), FilterOperator.String.Contains, "AM")).ShouldBe(["hammer", "Lamp"]);
        Matching(new ColumnCondition("Brand/Name", typeof(string), FilterOperator.String.StartsWith, "lu")).ShouldBe(["Lamp"]);
        Matching(new ColumnCondition("Price", typeof(decimal), FilterOperator.Number.GreaterThanOrEqual, 19m), new("Price", typeof(decimal), FilterOperator.Number.LessThanOrEqual, 35m))
            .ShouldBe(["hammer", "Lamp"]);
        Matching(new ColumnCondition("Kind", typeof(Kind), FilterOperator.Enum.Is, Kind.Hand), new("Kind", typeof(Kind), FilterOperator.Enum.Is, Kind.Power)).Count.ShouldBe(3);
        Matching(new ColumnCondition("Created", typeof(DateTimeOffset), FilterOperator.DateTime.Is, day)).ShouldBe(["hammer"]);
        Matching(new ColumnCondition("Created", typeof(DateTimeOffset), FilterOperator.DateTime.Before, day.AddDays(-14))).ShouldBe(["Lamp"]);
        Matching(new ColumnCondition("Note", typeof(string), FilterOperator.String.Empty, null)).ShouldBe(["Drill", "hammer"]);
    }

    private static List<string> Matching(params ColumnCondition[] conditions) =>
        [.. Rows.Where(ODataLinq.Filter<Gadget>(DataTableFilters.ToOData(conditions)!).Compile()).Select(r => r.Name)];

    [Theory]
    [InlineData("Tags/any(t: t eq 'x')")]
    [InlineData("Missing eq 1")]
    [InlineData("substringof('a',Name)")]
    public void What_it_cannot_answer_throws_not_supported(string filter) =>
        Should.Throw<NotSupportedException>(() => ODataLinq.Filter<Gadget>(filter));
}
