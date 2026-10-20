using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Reporting.Tests;

public sealed class GenerateReportTests(ReportingApp app) : ReportingTestBase(app)
{
    private const string Valid = """
        Here is your report:
        ```json
        {"widgets":[{"id":"rev","type":"Kpi","title":"Umsatz","x":0,"y":0,"w":3,"h":3,"query":{"entitySet":"Sales","aggregates":[{"function":"Sum","field":"Amount"}],"dateField":"SoldAt"},
          "options":{"format":"Currency","thresholds":[{"value":0,"color":"error"},{"value":1000,"color":"success","label":"Gut"}]}},
         {"id":"reg","type":"Chart","title":"Regionen","x":3,"y":0,"w":6,"h":5,"query":{"entitySet":"Sales","groupBy":{"field":"Region"}},"options":{"chart":"Pie"}}]}
        ```
        """;

    private const string UnknownField = """{"widgets":[{"id":"bad","type":"Chart","x":0,"y":0,"w":6,"h":5,"query":{"entitySet":"Sales","groupBy":{"field":"Nope"}}}]}""";

    private FakeAi Ai => App.App.Services.GetRequiredService<FakeAi>();

    private Task<HttpResponseMessage> GenerateAsync(string prompt, ReportDefinition? current = null, HttpClient? client = null) =>
        (client ?? Admin).PostAsJsonAsync("/api/v1/reports/generate", new GenerateReportRequest(prompt, current), Ct);

    [Fact]
    public async Task A_valid_answer_becomes_a_definition_that_is_not_saved()
    {
        Ai.Answer(Valid);

        var response = await GenerateAsync("Revenue and regions");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var definition = (await response.Content.ReadFromJsonAsync<ReportDefinition>(Ct))!;
        definition.Widgets.Select(w => w.Type).ShouldBe([WidgetType.Kpi, WidgetType.Chart]);
        definition.Widgets[0].Options.Thresholds[1].Label.ShouldBe("Gut");
        (await Admin.GetFromJsonAsync<List<ReportSummaryDto>>("/api/v1/reports", Ct))!.ShouldBeEmpty();
        var (instructions, input) = Ai.Calls.ShouldHaveSingleItem();
        instructions.ShouldContain("Sales \"Sales\"");
        instructions.ShouldContain("Status \"Status\" Enum Coworkee.Reporting.Tests.SaleStatus: Open|Shipped|Cancelled");
        instructions.ShouldNotContain("Secret");
        instructions.ShouldContain("12 columns");
        input.ShouldContain("Revenue and regions");
    }

    [Fact]
    public async Task An_unknown_field_is_sent_back_with_the_error_and_the_fixed_answer_wins()
    {
        Ai.Answer(UnknownField, Valid);

        var response = await GenerateAsync("Sales by region");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Ai.Calls.Count.ShouldBe(2);
        Ai.Calls[1].Input.ShouldContain("Sales has no field 'Nope'");
        Ai.Calls[1].Input.ShouldContain(UnknownField);
    }

    [Fact]
    public async Task Answers_that_stay_invalid_end_in_a_clear_error()
    {
        Ai.Answer(UnknownField, "no json at all", UnknownField);

        var response = await GenerateAsync("Sales by region");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("no usable report after 3 tries");
        Ai.Calls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Changing_a_report_sends_the_current_definition()
    {
        Ai.Answer(Valid);
        var current = new ReportDefinition { Widgets = [new ReportWidget { Id = "old", Type = WidgetType.Markdown, Options = new WidgetOptions { Text = "Hello" } }] };

        (await GenerateAsync("Add revenue", current)).StatusCode.ShouldBe(HttpStatusCode.OK);

        Ai.Calls.Single().Input.ShouldContain("\"id\":\"old\"");
    }

    [Fact]
    public async Task Without_a_model_or_the_permission_to_create_reports_it_is_refused()
    {
        Ai.Answer();
        (await GenerateAsync("Anything")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.GetFromJsonAsync<ReportAiStatusDto>("/api/v1/reports/ai", Ct))!.Available.ShouldBeFalse();

        Ai.Answer(Valid);
        var viewer = App.As((await CreateUserAsync("viewer@acme.test", ReportPermissions.View, ReportingApp.ViewSales)).Id, Setup.TenantId);
        (await GenerateAsync("Anything", client: viewer)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.GetFromJsonAsync<ReportAiStatusDto>("/api/v1/reports/ai", Ct))!.Available.ShouldBeTrue();
    }
}

public sealed class ReportAiToolTests
{
    [Fact]
    public void The_assistant_generates_reports_right_away_and_saves_them_only_after_a_confirmation()
    {
        var tools = new Coworkee.Ai.AiToolCatalog([], [new CoworkeeReportingModule()]).Tools.ToDictionary(t => t.Name);

        tools["generate_report"].ReadOnly.ShouldBeTrue();
        tools["generate_report"].Permissions.ShouldBe([ReportPermissions.Manage]);
        tools["create_report"].Destructive.ShouldBeTrue();
        tools["update_report"].Destructive.ShouldBeTrue();
        tools.Keys.ShouldNotContain("delete_report");
        new Coworkee.Ai.AiToolSchemas().Get(tools["generate_report"], System.Text.Json.JsonSerializerOptions.Web)["required"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldBe(["prompt"]);
    }
}
