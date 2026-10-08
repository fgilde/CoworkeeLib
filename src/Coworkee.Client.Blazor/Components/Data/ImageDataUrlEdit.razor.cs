using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>An image kept as data URL in a string property, e.g. a product picture.</summary>
public partial class ImageDataUrlEdit
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public long MaxBytes { get; set; } = 512 * 1024;

    private async Task LoadAsync(IBrowserFile? file)
    {
        if (file is null)
        {
            return;
        }

        if (file.Size > MaxBytes)
        {
            Snackbar.Add(L["The image may have at most {0} KB.", MaxBytes / 1024], Severity.Warning);
            return;
        }

        await using var stream = file.OpenReadStream(MaxBytes);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        await SetAsync($"data:{file.ContentType};base64,{Convert.ToBase64String(memory.ToArray())}");
    }

    private async Task SetAsync(string? value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(value);
    }
}
