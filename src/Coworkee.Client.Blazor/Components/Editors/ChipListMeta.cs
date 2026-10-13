using MudBlazor.Extensions.Components;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Components.Editors;

public static class ChipListMeta
{
#pragma warning disable BL0005 // MudEx configures the rendered editor and the grid item through these instances
    /// <summary>Edits an <c>IReadOnlyList&lt;string&gt;</c> property as chips over the full width of the form.</summary>
    public static void RenderAsChips(this ObjectEditPropertyMeta property, Action<ChipListEditor> configure)
    {
        property.RenderWith<ChipListEditor, IReadOnlyList<string>>(e => e.Value, configure);
        property.WrapInMudItem(i => i.xs = 12); // RenderWith drops the wrapper of the property
    }
#pragma warning restore BL0005
}
