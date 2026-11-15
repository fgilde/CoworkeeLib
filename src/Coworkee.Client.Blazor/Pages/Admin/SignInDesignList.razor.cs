using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>
/// The default sign-in design and the named ones as tiles on the page Sign-in, with the themes that use each; add, duplicate, rename and delete, and the
/// <see cref="SignInDesignCard"/> for the selected one. Changes stay in <see cref="Design"/> until the page saves.
/// </summary>
public partial class SignInDesignList
{
    private string? _selected;
    private IReadOnlyList<ThemeDto> _themes = [];

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public SignInDesign Design { get; set; } = null!;

    /// <summary>After every change of <see cref="Design"/>.</summary>
    [Parameter] public EventCallback Changed { get; set; }

    /// <summary>Further buttons of the design card.</summary>
    [Parameter] public RenderFragment? Actions { get; set; }

    // a design deleted elsewhere (or discarded) selects the default again
    private string? SelectedKey => Design.Find(_selected)?.Key;

    private SignInLook Selected => (SignInLook?)Design.Find(_selected) ?? Design;

    private string SelectedName => Selected is NamedSignInDesign named ? NameOf(named) : L["Default design"];

    private IEnumerable<(string? Key, string Name, SignInLook Look)> Tiles =>
        Design.Designs.Select(d => ((string?)d.Key, NameOf(d), (SignInLook)d)).Prepend((null, L["Default design"], Design));

    protected override Task OnInitializedAsync() => LoadThemesAsync();

    private async Task LoadThemesAsync()
    {
        try
        {
            _themes = await Api.GetThemesAsync();
        }
        catch (ApiException)
        {
            _themes = [];
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>The themes that show the design <paramref name="key"/>: those naming it, for the default design those naming none (or one that is gone).</summary>
    internal IReadOnlyList<string> ThemesOf(string? key) =>
        [.. _themes.Where(t => Design.Find(SignInDesign.KeyOf(t.Options))?.Key == key).Select(t => t.Name).Order(StringComparer.CurrentCultureIgnoreCase)];

    private string UsedBy(string? key) => ThemesOf(key) is { Count: > 0 } names ? L["Used by {0}", string.Join(", ", names)] : L["No theme uses it"];

    private string NameOf(NamedSignInDesign design) => string.IsNullOrWhiteSpace(design.Name) ? L["Unnamed design"] : design.Name;

    private Task NotifyAsync() => Changed.InvokeAsync();

    private void Select(string? key) => _selected = key;

    private Task AddAsync() => AddAsync(new NamedSignInDesign { Layout = SignInLayouts.Glass }, L["New design"]);

    private Task DuplicateAsync()
    {
        var copy = new NamedSignInDesign();
        copy.CopyLook(Selected);
        return AddAsync(copy, L["{0} (copy)", SelectedName]);
    }

    private Task AddAsync(NamedSignInDesign design, string name)
    {
        design.Key = NewKey();
        design.Name = UniqueName(name);
        Design.Designs.Add(design);
        _selected = design.Key;
        return NotifyAsync();
    }

    private async Task DeleteAsync()
    {
        if (Selected is not NamedSignInDesign named)
        {
            return;
        }

        var themes = ThemesOf(named.Key);
        var message = themes.Count > 0
            ? L["{0} use this design and show the default design once it is deleted (after saving).", string.Join(", ", themes)]
            : L["No theme uses this design. It goes away when the page is saved."];
        if (await Dialogs.ConfirmAsync(L["Delete {0}", NameOf(named)], message, L["Delete"], L["Cancel"], Icons.Material.Outlined.Delete))
        {
            Design.Designs.Remove(named);
            _selected = null;
            await NotifyAsync();
        }
    }

    // what themes store: short and stable, the name may change
    private string NewKey()
    {
        string key;
        do
        {
            key = "design-" + Guid.NewGuid().ToString("N")[..6];
        }
        while (Design.Find(key) is not null);

        return key;
    }

    private string UniqueName(string name)
    {
        var unique = name;
        for (var i = 2; Design.Designs.Any(d => string.Equals(d.Name, unique, StringComparison.CurrentCultureIgnoreCase)); i++)
        {
            unique = $"{name} {i}";
        }

        return unique;
    }
}
