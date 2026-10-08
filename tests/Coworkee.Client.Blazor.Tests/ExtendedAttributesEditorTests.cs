using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.ExtendedAttributes;
using Coworkee.Contracts.ExtendedAttributes;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ExtendedAttributesEditorTests : ClientTestBase
{
    private readonly IExtendedAttributesApi _api = Substitute.For<IExtendedAttributesApi>();
    private readonly Guid _id = Guid.CreateVersion7();

    public ExtendedAttributesEditorTests()
    {
        Services.AddSingleton(_api);
        _api.GetAsync("Documents", _id, default).ReturnsForAnyArgs([new ExtendedAttributeDto { Id = Guid.CreateVersion7(), Key = "Pages", Type = ExtendedAttributeType.Decimal, Decimal = 3 }]);
        _api.SaveAsync("Documents", _id, default!, default).ReturnsForAnyArgs(call => call.ArgAt<IReadOnlyList<ExtendedAttributeDto>>(2));
    }

    [Fact]
    public async Task Loads_the_attributes_adds_one_and_saves_the_whole_list()
    {
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
        var editor = Render<ExtendedAttributesEditor>(p => p.Add(e => e.EntityType, "Documents").Add(e => e.EntityId, _id));
        editor.WaitForAssertion(() => editor.Find("[data-attribute='Pages']").ShouldNotBeNull());

        await editor.Find("[data-testid='add-attribute']").ClickAsync(new());
        editor.FindAll("input[data-testid='attribute-key'], [data-testid='attribute-key'] input")[1].Change("Author");
        editor.Find("input[data-testid='attribute-text'], [data-testid='attribute-text'] input").Change("Ada");
        await editor.Find("[data-testid='save-attributes']").ClickAsync(new());

        await _api.Received(1).SaveAsync("Documents", _id,
            Arg.Is<IReadOnlyList<ExtendedAttributeDto>>(a => a.Count == 2 && a[1].Key == "Author" && a[1].Text == "Ada" && a[0].Decimal == 3), Arg.Any<CancellationToken>());
    }
}
