using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>An image kept as data URL in a string property, e.g. a product picture.</summary>
public partial class ImageDataUrlEdit
{
    private static readonly string[] MimeTypes = ["image/*"];

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public long MaxBytes { get; set; } = 512 * 1024;

    private async Task LoadAsync(UploadableFile? file)
    {
        if (file is null)
        {
            return;
        }

        if (file.Data is not { Length: > 0 } data || file.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
        {
            Snackbar.Add(L["The image could not be read."], Severity.Warning);
        }
        else if (data.Length > MaxBytes)
        {
            Snackbar.Add(L["The image may have at most {0} KB.", MaxBytes / 1024], Severity.Warning);
        }
        else
        {
            await SetAsync($"data:{file.ContentType};base64,{Convert.ToBase64String(data)}");
        }
    }
    private async Task SetAsync(string? value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(value);
    }
}
