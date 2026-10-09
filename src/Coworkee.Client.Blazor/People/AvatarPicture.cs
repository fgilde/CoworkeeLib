using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.People;

/// <summary>Reads a picture file and shrinks it in the browser to an avatar data URL; null when the browser cannot show the file.</summary>
public static class AvatarPicture
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;
    private const int Pixels = 256;

    public static async Task<string?> ReadAsync(IJSRuntime js, IBrowserFile file)
    {
        await using var stream = file.OpenReadStream(MaxUploadBytes);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
        return await module.InvokeAsync<string?>("resizeImage", $"data:{file.ContentType};base64,{Convert.ToBase64String(memory.ToArray())}", Pixels);
    }
}
