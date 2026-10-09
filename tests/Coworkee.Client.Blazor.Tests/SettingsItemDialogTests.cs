using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Customization;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SettingsItemDialogTests : ClientTestBase
{
    private static readonly ClientAppConfiguration Registration = new("Slots", "Slots", typeof(SlotsConfig), null);

    public SettingsItemDialogTests()
    {
        Services.AddSingleton(Registration);
        Services.AddSingleton<IComponentActivator, ReplacingComponentActivator>();
        Services.Configure<ComponentReplacementOptions>(o => o.ReplaceGeneric(typeof(MudExObjectEditDialog<>), typeof(SettingsItemDialog<>)));
        var values = JsonSerializer.SerializeToElement(new SlotsConfig(), JsonSerializerOptions.Web);
        Api.GetAppConfigurationAsync("Slots", Arg.Any<CancellationToken>()).Returns(new AppConfigurationValuesDto("Slots", values, values, [], null, null));
    }

    [Fact]
    public async Task A_list_item_dialog_edits_like_the_settings_form()
    {
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var editor = Render<AppConfigurationEditor<SlotsConfig>>(p => p.Add(e => e.Registration, Registration));

        var add = editor.WaitForElement("label.mud-button-text");
        _ = add.ClickAsync(new()); // completes when the dialog closes

        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='translations']"));
        dialogs.Markup.ShouldContain("Required");
        dialogs.Markup.ShouldNotContain("Display name");
        dialogs.FindAll(".mud-ex-property-reset-conatiner").ShouldBeEmpty();
        dialogs.Markup.ShouldNotContain(new GlobalResetSettings().ResetIcon);
    }

    [Fact]
    public void Forms_of_types_outside_the_settings_keep_their_computed_values()
    {
        SettingsItemMeta<RegistrationDocumentSlot>.Applies([Registration]).ShouldBeTrue();
        SettingsItemMeta<Unrelated>.Applies([Registration]).ShouldBeFalse();
    }

    public sealed class SlotsConfig
    {
        public List<RegistrationDocumentSlot> Documents { get; set; } = [];
    }

    public sealed class Unrelated
    {
        public string Name => "x";
    }
}
