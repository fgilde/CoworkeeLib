using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SettingsEditorsTests : ClientTestBase
{
    [Fact]
    public void Cron_editor_describes_the_value_and_takes_valid_expressions_only()
    {
        string? value = "0 6 * * *";
        var editor = Render<CronEditor>(p => p.Add(e => e.Value, value).Add(e => e.ValueChanged, v => value = v));

        editor.Markup.ShouldContain("Every day at 06:00 UTC");
        Expression(editor).Change("*/5 * * * *");
        value.ShouldBe("*/5 * * * *");
        editor.Markup.ShouldContain("Every 5 minutes");
        editor.Find(Input("cron-interval")).GetAttribute("value").ShouldBe("5");

        Expression(editor).Change("every day");
        value.ShouldBe("*/5 * * * *");
        editor.Markup.ShouldContain("Not a valid cron expression");
    }

    [Fact]
    public void Cron_editor_builds_the_expression_from_the_preset_fields()
    {
        string? value = "15 * * * *";
        var editor = Render<CronEditor>(p => p.Add(e => e.Value, value).Add(e => e.ValueChanged, v => value = v));

        editor.Find(Input("cron-minute")).Change("45");

        value.ShouldBe("45 * * * *");
        editor.Markup.ShouldContain("Every hour at minute 45");
    }

    [Fact]
    public async Task Content_types_editor_shows_names_adds_typed_types_and_removes_chips()
    {
        IReadOnlyList<string>? value = ["image/*", "application/pdf"];
        Render<MudPopoverProvider>();
        var editor = Render<ContentTypesEditor>(p => p.Add(e => e.Value, value).Add(e => e.ValueChanged, v => value = v));

        editor.Markup.ShouldContain("All images");
        editor.Markup.ShouldContain("PDF");

        await TypeAsync(editor, "Application/X-Custom");
        editor.WaitForAssertion(() => value.ShouldBe(["image/*", "application/pdf", "application/x-custom"]));

        await TypeAsync(editor, "pdf please");
        editor.WaitForAssertion(() => editor.Markup.ShouldContain("Not a file type"));

        await editor.Find("[data-type='image/*'] .mud-chip-close-button").ClickAsync(new());
        value.ShouldBe(["application/pdf", "application/x-custom"]);
    }

    [Fact]
    public void File_size_editor_shows_the_largest_fitting_unit_and_writes_bytes()
    {
        long? value = 5 * 1024 * 1024;
        var editor = Render<FileSizeEditor>(p => p.Add(e => e.Value, value).Add(e => e.ValueChanged, v => value = v));

        editor.Find(Input("file-size-amount")).GetAttribute("value").ShouldBe("5");

        editor.Find(Input("file-size-amount")).Change("1.5");
        value.ShouldBe(1536L * 1024);
    }

    [Fact]
    public async Task The_settings_form_renders_marked_properties_with_the_editors()
    {
        var config = new MarkedConfig { Schedule = "0 6 * * *", Types = ["image/*"], Limit = 1024 };
        var values = JsonSerializer.SerializeToElement(config, JsonSerializerOptions.Web);
        Api.GetAppConfigurationAsync("Marked", Arg.Any<CancellationToken>()).Returns(new AppConfigurationValuesDto("Marked", values, values, [], null, null));
        Api.SaveAppConfigurationAsync("Marked", Arg.Any<JsonElement>(), Arg.Any<CancellationToken>()).Returns(new AppConfigurationValuesDto("Marked", values, values, [], null, null));

        var editor = Render<AppConfigurationEditor<MarkedConfig>>(p => p.Add(e => e.Registration, new ClientAppConfiguration("Marked", "Marked", typeof(MarkedConfig), null)));

        editor.WaitForAssertion(() => editor.Find("[data-testid='cron']"));
        editor.Find("[data-testid='content-types']").InnerHtml.ShouldContain("All images");
        editor.Find("[data-testid='file-size']");

        editor.Find(Input("cron-expression")).Change("*/10 * * * *");
        await editor.Find("[data-type='image/*'] .mud-chip-close-button").ClickAsync(new());
        await editor.InvokeAsync(() => editor.Instance.SaveAsync());

        await Api.Received(1).SaveAppConfigurationAsync("Marked", Arg.Is<JsonElement>(v =>
            v.GetProperty("schedule").GetString() == "*/10 * * * *" && v.GetProperty("types").GetArrayLength() == 0 && v.GetProperty("limit").GetInt64() == 1024), Arg.Any<CancellationToken>());
    }

    private static string Input(string testId) => $"input[data-testid='{testId}'], [data-testid='{testId}'] input";

    private static AngleSharp.Dom.IElement Expression(IRenderedComponent<CronEditor> editor) => editor.Find(Input("cron-expression"));

    private static async Task TypeAsync(IRenderedComponent<ContentTypesEditor> editor, string text)
    {
        editor.Find(Input("content-type-input")).Input(text);
        await editor.Find(Input("content-type-input")).KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
    }

    public sealed class MarkedConfig
    {
        [Cron]
        public string Schedule { get; set; } = string.Empty;

        [ContentTypes]
        public List<string> Types { get; set; } = [];

        [FileSize]
        public long? Limit { get; set; }
    }
}
