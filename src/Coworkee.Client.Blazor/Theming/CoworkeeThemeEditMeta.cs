using Coworkee.Client.Blazor.Localization;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Theming;

/// <summary>
/// MudEx applies it to every object form of a <see cref="CoworkeeTheme"/>, the theme editor included: <see cref="CoworkeeTheme.Logo"/> gets the
/// <see cref="ThemeLogoEdit"/> and <see cref="CoworkeeTheme.CustomCss"/> is a stylesheet, so it gets the <see cref="CssRulesEditor"/> (rules or raw CSS).
/// An app with its own theme class derives from it, overrides <see cref="ConfigureAsync"/>, calls the base and registers both with AddCoworkeeTheme.
/// </summary>
public class CoworkeeThemeEditMeta<TTheme>(CoworkeeLocalizer l) : IObjectMetaConfiguration<TTheme>
    where TTheme : CoworkeeTheme
{
    protected CoworkeeLocalizer L { get; } = l;

#pragma warning disable BL0005, MUD0012 // MudEx binds and configures the rendered editor through this instance
    public virtual Task ConfigureAsync(ObjectEditMeta<TTheme> meta)
    {
        meta.Property(t => t.Logo).RenderWith<ThemeLogoEdit, string?>(f => f.Value).WrapInMudItem(i => i.md = 12);
        meta.Property(t => t.CustomCss).WithLabel(L["Custom CSS"]).RenderWith<CssRulesEditor, string?>(f => f.Value, f => f.HelperText = L["Plain CSS for the whole app, applied after the theme"]).WrapInMudItem(i => i.md = 12);
        return Task.CompletedTask;
    }
#pragma warning restore BL0005, MUD0012
}

/// <inheritdoc />
public class CoworkeeThemeEditMeta(CoworkeeLocalizer l) : CoworkeeThemeEditMeta<CoworkeeTheme>(l);
