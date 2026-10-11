using Coworkee.Client.Blazor.Components.Query;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

public sealed class QueryTranslationTests
{
    // Sunday, 11 October 2026, 15:30 in UTC+2
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 15, 30, 0, TimeSpan.FromHours(2));

    private static readonly Guid Acme = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly QueryField[] Fields =
    [
        new("Name", "Name", QueryFieldType.Text),
        new("Total", "Total", QueryFieldType.Number),
        new("Items", "Items", QueryFieldType.Number),
        new("Paid", "Paid", QueryFieldType.Boolean),
        new("Status", "Status", QueryFieldType.Enum) { Options = [new("Open", "Open"), new("Shipped", "Shipped out"), new("Cancelled", "Cancelled")] },
        new("At", "Ordered", QueryFieldType.DateTime),
        new("Due", "Due", QueryFieldType.Date) { Nullable = true },
        new("CustomerId", "Customer", QueryFieldType.Guid) { Options = [new(Acme.ToString(), "Acme")] },
        new("Customer.Name", "Customer name", QueryFieldType.Text),
        new("Perks", "Perks", QueryFieldType.Enum) { Flags = true, Options = [new("Express", "Express"), new("Gift", "Gift")] },
    ];

    private static readonly Row[] Rows =
    [
        new("Drill", 99.5m, 1, true, Status.Open, Now.AddHours(-1), new DateOnly(2026, 10, 20), Acme, new Customer("Acme"), Perks.Express | Perks.Gift),
        new("Hammer", 19m, 3, false, Status.Shipped, Now.AddDays(-3), null, Guid.Empty, null, Perks.Gift),
        new("O'Neil lamp", 35m, 2, true, Status.Cancelled, Now.AddDays(-40), new DateOnly(2026, 10, 11), Acme, new Customer("acme north")),
        new(null, 1000m, 12, false, Status.Shipped, Now.AddYears(-1), new DateOnly(2025, 1, 1), Guid.Empty, new Customer("Zeta"), Perks.Express),
    ];

    public static TheoryData<QueryRule, string, string[]> Cases => new()
    {
        { Rule("Name", QueryOperator.Contains, "LAMP"), "contains(tolower(Name),'lamp')", ["O'Neil lamp"] },
        { Rule("Name", QueryOperator.NotContains, "r"), "(Name eq null or not contains(tolower(Name),'r'))", ["O'Neil lamp", "-"] },
        { Rule("Name", QueryOperator.StartsWith, "o'"), "startswith(tolower(Name),'o''')", ["O'Neil lamp"] },
        { Rule("Name", QueryOperator.EndsWith, "ER"), "endswith(tolower(Name),'er')", ["Hammer"] },
        { Rule("Name", QueryOperator.Equal, "Drill"), "Name eq 'Drill'", ["Drill"] },
        { Rule("Name", QueryOperator.NotEqual, "Drill"), "Name ne 'Drill'", ["Hammer", "O'Neil lamp", "-"] },
        { Rule("Name", QueryOperator.In, values: ["Drill", "Hammer"]), "Name in ('Drill','Hammer')", ["Drill", "Hammer"] },
        { Rule("Name", QueryOperator.NotIn, values: ["Drill", "Hammer"]), "not (Name in ('Drill','Hammer'))", ["O'Neil lamp", "-"] },
        { Rule("Name", QueryOperator.IsNull), "(Name eq null or Name eq '')", ["-"] },
        { Rule("Name", QueryOperator.IsNotNull), "(Name ne null and Name ne '')", ["Drill", "Hammer", "O'Neil lamp"] },
        { Rule("Total", QueryOperator.Equal, "99.5"), "Total eq 99.5", ["Drill"] },
        { Rule("Total", QueryOperator.NotEqual, "99.5"), "Total ne 99.5", ["Hammer", "O'Neil lamp", "-"] },
        { Rule("Total", QueryOperator.GreaterThan, "35"), "Total gt 35", ["Drill", "-"] },
        { Rule("Total", QueryOperator.GreaterThanOrEqual, "35"), "Total ge 35", ["Drill", "O'Neil lamp", "-"] },
        { Rule("Total", QueryOperator.LessThan, "35"), "Total lt 35", ["Hammer"] },
        { Rule("Total", QueryOperator.LessThanOrEqual, "35"), "Total le 35", ["Hammer", "O'Neil lamp"] },
        { Rule("Total", QueryOperator.Between, "19", "99.5"), "(Total ge 19 and Total le 99.5)", ["Drill", "Hammer", "O'Neil lamp"] },
        { Rule("Total", QueryOperator.NotBetween, "19", "99.5"), "(Total lt 19 or Total gt 99.5)", ["-"] },
        { Rule("Items", QueryOperator.In, values: ["1", "12"]), "Items in (1,12)", ["Drill", "-"] },
        { Rule("Items", QueryOperator.NotIn, values: ["1", "12"]), "not (Items in (1,12))", ["Hammer", "O'Neil lamp"] },
        { Rule("Paid", QueryOperator.IsTrue), "Paid eq true", ["Drill", "O'Neil lamp"] },
        { Rule("Paid", QueryOperator.IsFalse), "Paid eq false", ["Hammer", "-"] },
        { Rule("Status", QueryOperator.Equal, "Shipped"), "Status eq 'Shipped'", ["Hammer", "-"] },
        { Rule("Status", QueryOperator.NotEqual, "Shipped"), "Status ne 'Shipped'", ["Drill", "O'Neil lamp"] },
        { Rule("Status", QueryOperator.In, values: ["Open", "Cancelled"]), "(Status eq 'Open' or Status eq 'Cancelled')", ["Drill", "O'Neil lamp"] },
        { Rule("Status", QueryOperator.NotIn, values: ["Open", "Cancelled"]), "not (Status eq 'Open' or Status eq 'Cancelled')", ["Hammer", "-"] },
        { Rule("At", QueryOperator.Today), "(At ge 2026-10-11T00:00:00+02:00 and At lt 2026-10-12T00:00:00+02:00)", ["Drill"] },
        { Rule("At", QueryOperator.ThisWeek), "(At ge 2026-10-05T00:00:00+02:00 and At lt 2026-10-12T00:00:00+02:00)", ["Drill", "Hammer"] },
        { Rule("At", QueryOperator.ThisMonth), "(At ge 2026-10-01T00:00:00+02:00 and At lt 2026-11-01T00:00:00+02:00)", ["Drill", "Hammer"] },
        { Rule("At", QueryOperator.ThisYear), "(At ge 2026-01-01T00:00:00+02:00 and At lt 2027-01-01T00:00:00+02:00)", ["Drill", "Hammer", "O'Neil lamp"] },
        { Rule("At", QueryOperator.InLast, "7"), "(At ge 2026-10-04T00:00:00+02:00 and At lt 2026-10-12T00:00:00+02:00)", ["Drill", "Hammer"] },
        { Rule("At", QueryOperator.InNext, "7"), "(At ge 2026-10-11T00:00:00+02:00 and At lt 2026-10-19T00:00:00+02:00)", ["Drill"] },
        { Rule("At", QueryOperator.Equal, "2026-10-08"), "(At ge 2026-10-08T00:00:00+02:00 and At lt 2026-10-09T00:00:00+02:00)", ["Hammer"] },
        { Rule("At", QueryOperator.NotEqual, "2026-10-08"), "(At lt 2026-10-08T00:00:00+02:00 or At ge 2026-10-09T00:00:00+02:00)", ["Drill", "O'Neil lamp", "-"] },
        { Rule("At", QueryOperator.LessThan, "2026-10-08"), "At lt 2026-10-08T00:00:00+02:00", ["O'Neil lamp", "-"] },
        { Rule("At", QueryOperator.LessThanOrEqual, "2026-10-08"), "At lt 2026-10-09T00:00:00+02:00", ["Hammer", "O'Neil lamp", "-"] },
        { Rule("At", QueryOperator.GreaterThan, "2026-10-08"), "At ge 2026-10-09T00:00:00+02:00", ["Drill"] },
        { Rule("At", QueryOperator.GreaterThanOrEqual, "2026-10-08"), "At ge 2026-10-08T00:00:00+02:00", ["Drill", "Hammer"] },
        { Rule("At", QueryOperator.Between, "2026-09-01", "2026-10-08"), "(At ge 2026-09-01T00:00:00+02:00 and At lt 2026-10-09T00:00:00+02:00)", ["Hammer", "O'Neil lamp"] },
        { Rule("At", QueryOperator.NotBetween, "2026-09-01", "2026-10-08"), "(At lt 2026-09-01T00:00:00+02:00 or At ge 2026-10-09T00:00:00+02:00)", ["Drill", "-"] },
        { Rule("Due", QueryOperator.Equal, "2026-10-11"), "(Due ge 2026-10-11 and Due lt 2026-10-12)", ["O'Neil lamp"] },
        { Rule("Due", QueryOperator.Today), "(Due ge 2026-10-11 and Due lt 2026-10-12)", ["O'Neil lamp"] },
        { Rule("Due", QueryOperator.GreaterThan, "2026-10-11"), "Due ge 2026-10-12", ["Drill"] },
        { Rule("Due", QueryOperator.IsNull), "Due eq null", ["Hammer"] },
        { Rule("Due", QueryOperator.IsNotNull), "Due ne null", ["Drill", "O'Neil lamp", "-"] },
        { Rule("CustomerId", QueryOperator.Equal, Acme.ToString()), $"CustomerId eq {Acme}", ["Drill", "O'Neil lamp"] },
        { Rule("CustomerId", QueryOperator.In, values: [Acme.ToString(), Guid.Empty.ToString()]), $"CustomerId in ({Acme},{Guid.Empty})", ["Drill", "Hammer", "O'Neil lamp", "-"] },
        { Rule("Customer.Name", QueryOperator.StartsWith, "ACME"), "startswith(tolower(Customer/Name),'acme')", ["Drill", "O'Neil lamp"] },
        { Rule("Customer.Name", QueryOperator.IsNull), "(Customer/Name eq null or Customer/Name eq '')", ["Hammer"] },
        { Rule("Perks", QueryOperator.Equal, "Gift"), "Perks has 'Gift'", ["Drill", "Hammer"] },
        { Rule("Perks", QueryOperator.NotEqual, "Gift"), "not (Perks has 'Gift')", ["O'Neil lamp", "-"] },
        { Rule("Perks", QueryOperator.In, values: ["Express", "Gift"]), "(Perks has 'Express' or Perks has 'Gift')", ["Drill", "Hammer", "-"] },
        { Rule("Perks", QueryOperator.NotIn, values: ["Express", "Gift"]), "not (Perks has 'Express' or Perks has 'Gift')", ["O'Neil lamp"] },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_operator_reads_the_same_as_odata_and_as_linq(QueryRule rule, string odata, string[] expected)
    {
        var query = new QueryGroup { Children = [rule] };

        var filter = ODataQueryTranslator.ToFilter(query, Fields, Now);
        var linq = Rows.Where(query, Fields, Now).Select(Show).ToArray();
        var local = Rows.Where(ODataLinq.Filter<Row>(filter!).Compile()).Select(Show).ToArray();

        filter.ShouldBe(odata);
        linq.ShouldBe(expected);
        local.ShouldBe(expected, "the client entities' OData reads the filter the same way");
    }

    [Fact]
    public void Nested_groups_keep_their_precedence_and_negation()
    {
        var query = new QueryGroup
        {
            Logic = QueryLogic.Or,
            Children =
            [
                Rule("Status", QueryOperator.Equal, "Open"),
                new QueryGroup
                {
                    Not = true,
                    Children = [Rule("Paid", QueryOperator.IsTrue), new QueryGroup { Logic = QueryLogic.Or, Children = [Rule("Total", QueryOperator.LessThan, "20"), Rule("Name", QueryOperator.IsNull)] }],
                },
            ],
        };

        var filter = ODataQueryTranslator.ToFilter(query, Fields, Now);

        filter.ShouldBe("(Status eq 'Open' or not (Paid eq true and (Total lt 20 or (Name eq null or Name eq ''))))");
        Rows.Where(query, Fields, Now).Select(Show).ShouldBe(["Drill", "Hammer", "O'Neil lamp", "-"]);
        Rows.Where(ODataLinq.Filter<Row>(filter!).Compile()).Select(Show).ShouldBe(["Drill", "Hammer", "O'Neil lamp", "-"]);
        QueryDescriber.Describe(query, Fields).ShouldBe("Status is Open or not (Paid is yes and (Total is less than 20 or Name is empty))");
    }

    [Fact]
    public void Incomplete_rules_and_empty_groups_filter_nothing()
    {
        var query = new QueryGroup { Children = [new QueryRule { Field = "Total", Operator = QueryOperator.Between, Value = "1" }, new QueryGroup(), Rule("Nope", QueryOperator.IsNull)] };

        ODataQueryTranslator.ToFilter(query, Fields, Now).ShouldBeNull();
        Rows.Where(query, Fields, Now).Count().ShouldBe(Rows.Length);
        query.IsEmpty.ShouldBeFalse();
        new QueryGroup().IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Queries_round_trip_as_json_with_names_for_enums()
    {
        var query = new QueryGroup
        {
            Logic = QueryLogic.Or,
            Not = true,
            Children =
            [
                Rule("Status", QueryOperator.In, values: ["Open", "Shipped"]),
                new QueryGroup { Children = [Rule("Total", QueryOperator.Between, "1.5", "20"), Rule("At", QueryOperator.InLast, "30")] },
                new QueryRule { Field = "CustomerId", Operator = QueryOperator.Equal, Value = Acme.ToString(), Labels = new() { [Acme.ToString()] = "Acme" } },
            ],
        };

        var json = QueryJson.Serialize(query);
        var back = QueryJson.Deserialize(json)!;

        json.ShouldContain("\"logic\":\"Or\"");
        json.ShouldContain("\"operator\":\"In\"");
        json.ShouldContain("\"kind\":\"group\"");
        json.ShouldNotContain("value2\":null");
        QueryJson.Serialize(back).ShouldBe(json);
        ODataQueryTranslator.ToFilter(back, Fields, Now).ShouldBe(ODataQueryTranslator.ToFilter(query, Fields, Now));
        QueryDescriber.Describe(back, Fields).ShouldContain("Customer is Acme");
        QueryJson.Deserialize("{not json").ShouldBeNull();
        QueryJson.Deserialize("""{"kind":"group","children":[{"kind":"rule","operator":"Nope"}]}""").ShouldBeNull();
    }

    [Fact]
    public void Linq_runs_on_a_queryable_with_null_navigations()
    {
        var query = new QueryGroup { Children = [Rule("Customer.Name", QueryOperator.Contains, "acme"), Rule("Total", QueryOperator.GreaterThan, "50")] };

        Rows.AsQueryable().Where(query, Fields, Now).Select(Show).ShouldBe(["Drill"]);
        LinqQueryTranslator.ToExpression<Row>(new QueryGroup(), Fields).Compile()(Rows[0]).ShouldBeTrue();
    }

    [Fact]
    public void Operators_fit_the_field_and_read_by_type()
    {
        QueryOperators.For(Fields[3]).ShouldBe([QueryOperator.IsTrue, QueryOperator.IsFalse]);
        QueryOperators.For(Fields[4])[0].ShouldBe(QueryOperator.In);
        QueryOperators.For(Fields[6]).ShouldContain(QueryOperator.IsNull);
        QueryOperators.For(Fields[1]).ShouldNotContain(QueryOperator.IsNull);
        QueryOperators.Label(QueryOperator.GreaterThan, QueryFieldType.Date).ShouldBe("is after");
        QueryOperators.Label(QueryOperator.GreaterThan, QueryFieldType.Number).ShouldBe("is greater than");
        QueryOperators.IsComplete(Rule("Total", QueryOperator.Between, "1")).ShouldBeFalse();
        QueryOperators.IsComplete(Rule("At", QueryOperator.Today)).ShouldBeTrue();
    }

    private static QueryRule Rule(string field, QueryOperator op, string? value = null, string? value2 = null, List<string>? values = null) =>
        new() { Field = field, Operator = op, Value = value, Value2 = value2, Values = values };

    private static string Show(Row row) => row.Name ?? "-";

    public enum Status
    {
        Open,
        Shipped,
        Cancelled,
    }

    public sealed record Customer(string Name);

    [Flags]
    public enum Perks
    {
        None = 0,
        Express = 1,
        Gift = 2,
    }

    public sealed record Row(string? Name, decimal Total, int Items, bool Paid, Status Status, DateTimeOffset At, DateOnly? Due, Guid CustomerId, Customer? Customer, Perks Perks = Perks.None);
}
