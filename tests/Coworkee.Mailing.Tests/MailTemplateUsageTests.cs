using System.Text.Json.Nodes;

namespace Coworkee.Mailing.Tests;

public sealed class MailTemplateUsageTests
{
    private const string Sample = """
        {
          "user": { "first_name": "Ada", "email": "ada@example.com" },
          "unsubscribe_url": "https://x/unsubscribe",
          "shipment": {
            "number": "S1",
            "eta": "2026-11-20T10:00:00Z",
            "notes": "long text",
            "port": { "code": "DEHAM", "name": "Hamburg" },
            "containers": [ { "number": "C1", "seal": "X", "weight": 12.5 } ]
          },
          "confirm_url": "https://x/confirm"
        }
        """;

    private static JsonObject SendModel(params string[] texts) => (JsonObject)JsonNode.Parse(MailTemplateUsage.SendModel(Sample, MailTemplateUsage.Read(texts)))!;

    [Fact]
    public void Only_the_members_the_template_reads_are_asked_for()
    {
        var model = SendModel("Shipment {{ shipment.number }}", "<p>ETA {{ shipment.eta | date.to_string '%d.%m.%Y' }} in {{ shipment.port.name }}</p>");

        model.ToJsonString().ShouldBe("""{"shipment":{"number":"S1","eta":"2026-11-20T10:00:00Z","port":{"name":"Hamburg"}}}""");
    }

    [Fact]
    public void Members_of_a_loop_variable_cut_the_list_items()
    {
        var model = SendModel("{{ for c in shipment.containers }}{{ c.number }}{{ end }}");

        model.ToJsonString().ShouldBe("""{"shipment":{"containers":[{"number":"C1"}]}}""");
    }

    [Fact]
    public void An_object_read_whole_keeps_all_its_members()
    {
        var model = SendModel("{{ shipment.port }}");

        model["shipment"]!["port"]!.ToJsonString().ShouldBe("""{"code":"DEHAM","name":"Hamburg"}""");
        model["shipment"]!.AsObject().Count.ShouldBe(1);
    }

    [Fact]
    public void Values_filled_per_recipient_are_never_asked_for()
    {
        var model = SendModel("Hello {{ user.first_name }}, <a href=\"{{ unsubscribe_url }}\">unsubscribe</a> {{ confirm_url }}");

        model.ToJsonString().ShouldBe("""{"confirm_url":"https://x/confirm"}""");
    }

    [Fact]
    public void Conditions_and_indexes_count_as_reads()
    {
        var model = SendModel("{{ if shipment.notes != empty }}{{ shipment.containers[0].seal }}{{ end }}");

        model.ToJsonString().ShouldBe("""{"shipment":{"notes":"long text","containers":[{"seal":"X"}]}}""");
    }

    [Fact]
    public void A_loop_variable_does_not_leak_out_of_its_loop()
    {
        var usage = MailTemplateUsage.Read(["{{ for shipment in list }}{{ shipment.number }}{{ end }}{{ shipment.eta }}"]);

        usage.Whole.ShouldBe(["list[].number", "shipment.eta"], ignoreOrder: true);
        usage.Iterated.ShouldBe(["list"]);
    }

    [Fact]
    public void A_template_that_reads_nothing_of_the_model_asks_for_nothing()
    {
        SendModel("Static text", "{{ 'x' | string.upcase }}").ToJsonString().ShouldBe("{}");
    }

    [Fact]
    public void A_broken_text_adds_nothing()
    {
        SendModel("{{ end }}{{ shipment.number }}", "{{ confirm_url }}").ToJsonString().ShouldBe("""{"confirm_url":"https://x/confirm"}""");
    }
}
