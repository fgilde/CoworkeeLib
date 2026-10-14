using MudBlazor.Extensions.Components;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Components.Editors;

public static class ChipListMeta
{
#pragma warning disable BL0005 // MudEx configures the rendered editor and the grid item through these instances
    /// <summary>Edits an <c>IReadOnlyList&lt;string&gt;</c> property as chips over the full width of the form; Enter, a space, a comma, a paste or leaving the field adds them.</summary>
    public static void RenderAsChips(this ObjectEditPropertyMeta property, Action<MudExTagField<string>> configure)
    {
        property.RenderWith<MudExTagField<string>, IReadOnlyList<string>, List<string>>(e => e.Values, e =>
        {
            e.Delimiters = [' ', ','];
            e.CommitOnBlur = true;
            configure(e);
        }, v => [.. v ?? []], v => v ?? []);
        property.WrapInMudItem(i => i.xs = 12);
    }
#pragma warning restore BL0005
}
