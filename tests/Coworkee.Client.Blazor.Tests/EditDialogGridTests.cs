using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Client.Blazor.Data;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;

namespace Coworkee.Client.Blazor.Tests;

public sealed class EditDialogGridTests : ClientTestBase
{
    private static readonly Guid Contoso = Guid.NewGuid();

    [Fact]
    public void Picker_shows_its_label_and_the_selected_text_in_the_density_of_other_inputs()
    {
        Services.AddSingleton<IODataClient>(Brands());
        Render<MudPopoverProvider>();

        var picker = Render<ODataPicker>(p => p.Add(e => e.EntitySet, "Brands").Add(e => e.Value, Contoso).Add(e => e.Label, "Brand").Add(e => e.Required, true));

        picker.WaitForAssertion(() => picker.Find("input").GetAttribute("value").ShouldBe("Contoso"));
        picker.Find("label").TextContent.ShouldContain("Brand");
        picker.Find(".mud-input-control").ClassList.ShouldContain("mud-input-control-margin-dense");
    }

    [Fact]
    public async Task The_dialog_keeps_editors_set_with_RenderWith_in_the_grid_and_localizes_enums()
    {
        Services.AddSingleton<IODataClient>(Brands());
        Render<MudPopoverProvider>();
        var provider = Render<MudDialogProvider>();
        var model = new ProductForm { Name = "Phone", BrandId = Contoso, Kind = ProductKind.GiftWrap };

        var shown = provider.InvokeAsync(() => Services.GetRequiredService<IDialogService>().ShowEditAsync("Edit product", model, meta =>
            meta.Property(p => p.BrandId).WithLabel("Brand").RenderWith<ODataPicker, Guid>(e => e.Value).WithAdditionalAttribute(nameof(ODataPicker.EntitySet), "Brands")));

        await Task.WhenAny(shown, Task.Delay(3000));
        shown.Exception?.ToString().ShouldBeNull();
        provider.WaitForAssertion(() => provider.Find("[data-testid='picker-Brands']"));
        provider.Find("[data-testid='picker-Brands']").Closest(".mud-grid-item").ShouldNotBeNull();
        provider.FindAll(".mud-grid-item").Count.ShouldBeGreaterThanOrEqualTo(3);

        provider.WaitForAssertion(() => provider.FindComponent<EnumEditor<ProductKind>>().Find("input").GetAttribute("value").ShouldBe("Gift wrap"));
        provider.FindComponent<NullableEnumEditor<ProductKind>>().Find("input").GetAttribute("value").ShouldBe("Hardware");
    }

    [Fact]
    public void A_flags_editor_offers_single_members_only()
    {
        var editor = Render<EnumEditor<Channels>>(p => p.Add(e => e.Value, Channels.All));

        editor.Find("input").GetAttribute("value").ShouldBe("Email, Sms");
    }

    [Flags]
    private enum Channels
    {
        None = 0,
        Email = 1,
        Sms = 2,
        All = Email | Sms,
    }

    private static FakeODataClient Brands() => new FakeODataClient().With("Brands", JsonSerializer.SerializeToElement(new { Id = Contoso, Name = "Contoso" }));

    private enum ProductKind
    {
        Hardware,
        [Display(Name = "Gift wrap")]
        GiftWrap,
    }

    private sealed class ProductForm
    {
        public string Name { get; set; } = string.Empty;

        public Guid BrandId { get; set; }

        public ProductKind Kind { get; set; }

        public ProductKind? Fallback { get; set; } = ProductKind.Hardware;
    }
}