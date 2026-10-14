using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using MudBlazor;
using MudBlazor.Extensions.Components;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SettingsEditorsTests : ClientTestBase
{
    [Fact]
    public async Task Content_types_editor_shows_names_picks_from_the_catalog_and_removes_chips()
    {
        IReadOnlyList<string>? value = ["image/*", "application/x-custom"];
        Render<MudPopoverProvider>();
        var editor = Render<ContentTypesEditor>(p => p.Add(e => e.Value, value).Add(e => e.ValueChanged, v => value = v));

        editor.Markup.ShouldContain("All images");
        var search = editor.FindComponent<MudAutocomplete<string>>();
        (await search.Instance.SearchFunc!("pdf", CancellationToken.None)).ShouldContain("application/pdf");
        (await search.Instance.SearchFunc!(null, CancellationToken.None)).ShouldNotContain("image/*"); // picked types are not offered again

        await editor.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("application/pdf"));
        value.ShouldBe(["image/*", "application/x-custom", "application/pdf"]); // types the catalog does not know stay

        await editor.Find("[data-type='image/*'] .mud-chip-close-button").ClickAsync(new());
        value.ShouldBe(["application/x-custom", "application/pdf"]);
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

        editor.WaitForAssertion(() => editor.FindComponent<MudExCronEdit>());
        editor.Find("[data-testid='content-types']").InnerHtml.ShouldContain("All images");
        editor.Find("[data-testid='file-size']");

        editor.FindComponent<MudExCronEdit>().FindComponents<MudTextField<string>>()[^1].Find("input").Change("*/10 * * * *");
        await editor.Find("[data-type='image/*'] .mud-chip-close-button").ClickAsync(new());
        await editor.InvokeAsync(() => editor.Instance.SaveAsync());

        await Api.Received(1).SaveAppConfigurationAsync("Marked", Arg.Is<JsonElement>(v =>
            v.GetProperty("schedule").GetString() == "*/10 * * * *" && v.GetProperty("types").GetArrayLength() == 0 && v.GetProperty("limit").GetInt64() == 1024), Arg.Any<CancellationToken>());
    }

    private static string Input(string testId) => $"input[data-testid='{testId}'], [data-testid='{testId}'] input";

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
