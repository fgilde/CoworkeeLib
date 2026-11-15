using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Theming;

/// <summary>
/// Edits <see cref="CoworkeeTheme.SignInDesign"/>: the default design or one of the named designs of the admin page Sign-in. Reading the designs needs
/// Settings.Manage; without it the choice keeps what the theme has.
/// </summary>
public partial class SignInDesignSelect
{
    private IReadOnlyList<NamedSignInDesign> _designs = [];

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (!Options.AllowSignInDesign)
        {
            return;
        }

        try
        {
            _designs = (await Api.GetAppConfigurationAsync(SignInDesign.Section)).Values.Deserialize<SignInDesign>(JsonSerializerOptions.Web)?.Designs ?? [];
        }
        catch (ApiException)
        {
            _designs = [];
        }
    }

    private async Task SetAsync(string? value)
    {
        Value = string.IsNullOrEmpty(value) ? null : value;
        await ValueChanged.InvokeAsync(Value);
    }
}
