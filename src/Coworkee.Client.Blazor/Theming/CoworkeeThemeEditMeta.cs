using Coworkee.Client.Blazor.Localization;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Theming;

/// <summary>
/// MudEx applies it to every object form of a <see cref="CoworkeeTheme"/>, the theme editor included: <see cref="CoworkeeTheme.Logo"/> gets the
/// <see cref="ThemeLogoEdit"/> and <see cref="CoworkeeTheme.CustomCss"/> is a stylesheet, so it gets a growing multiline field. Register another IObjectMetaConfiguration&lt;CoworkeeTheme&gt; to replace it.
/// </summary>
public sealed class CoworkeeThemeEditMeta(CoworkeeLocalizer l) : IObjectMetaConfiguration<CoworkeeTheme>
{
#pragma warning disable BL0005, MUD0012 // MudEx binds and configures the rendered editor through this instance
    public Task ConfigureAsync(ObjectEditMeta<CoworkeeTheme> meta)
    {
        meta.Property(t => t.Logo).RenderWith<ThemeLogoEdit, string?>(f => f.Value).WrapInMudItem(i => i.md = 12);
        meta.Property(t => t.CustomCss).RenderWith<MudTextField<string>, string?>(f => f.Value, f =>
        {
            f.Lines = 6;
            f.MaxLines = 24;
            f.Variant = Variant.Outlined;
            f.HelperText = l["Plain CSS for the whole app, applied after the theme"];
        });
        return Task.CompletedTask;
    }
#pragma warning restore BL0005, MUD0012
}
