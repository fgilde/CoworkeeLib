using Coworkee.Contracts.Query;
using Coworkee.OData.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.Edm;

namespace Coworkee.OData.Tests;

/// <summary>The query model on the server: the fields of a set come from its EDM model, a saved query becomes a $filter at request time.</summary>
public sealed class EdmQueryFieldsTests(ODataApp app)
{
    private IReadOnlyList<QueryField> Fields(string set) => EdmQueryFields.For(app.App.Services.GetRequiredService<IEdmModel>(), set);

    [Fact]
    public void Fields_of_a_set_cover_its_properties_with_their_kinds()
    {
        var widgets = Fields("Widgets").ToDictionary(f => f.Name);

        widgets["Name"].Type.ShouldBe(QueryFieldType.Text);
        widgets["Price"].Type.ShouldBe(QueryFieldType.Number);
        widgets["MadeAt"].Type.ShouldBe(QueryFieldType.DateTime);
        widgets["Active"].Type.ShouldBe(QueryFieldType.Boolean);
        widgets["Id"].Type.ShouldBe(QueryFieldType.Guid);
        widgets["Kind"].Type.ShouldBe(QueryFieldType.Enum);
        widgets["Kind"].Options!.Select(o => o.Value).ShouldContain("Hammer");
        widgets["Features"].Flags.ShouldBeTrue();
        widgets["Kind"].Flags.ShouldBeFalse();

        // a property only some users see is still a field; one that is hidden is not part of the model
        var gadgets = Fields("Gadgets").Select(f => f.Name).ToList();
        gadgets.ShouldContain("Category");
        gadgets.ShouldNotContain("SerialCode");
    }

    [Fact]
    public void A_saved_query_translates_on_the_server_with_relative_dates()
    {
        var query = new QueryGroup
        {
            Children =
            [
                new QueryRule { Field = "MadeAt", Operator = QueryOperator.InLast, Value = "7" },
                new QueryRule { Field = "Kind", Operator = QueryOperator.Equal, Value = "Hammer" },
            ],
        };
        var now = new DateTimeOffset(2026, 11, 26, 15, 30, 0, TimeSpan.Zero);

        var filter = ODataQueryTranslator.ToFilter(QueryJson.Deserialize(QueryJson.Serialize(query))!, Fields("Widgets"), now);

        filter.ShouldBe("((MadeAt ge 2026-11-19T00:00:00+00:00 and MadeAt lt 2026-11-27T00:00:00+00:00) and Kind eq 'Hammer')");
    }
}
