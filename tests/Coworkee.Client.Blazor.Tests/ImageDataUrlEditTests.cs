using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ImageDataUrlEditTests : ClientTestBase
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void Preset_value()
    {
        Render<MudPopoverProvider>();
        var edit = Render<ImageDataUrlEdit>(p => p.Add(e => e.Value, "data:image/png;base64,AAAA"));
        edit.FindAll("img").Count.ShouldBe(1);
    }

    [Fact]
    public void A_file_picked_with_the_upload_field_becomes_a_data_url()
    {
        string? value = null;
        Render<MudPopoverProvider>();
        var edit = Render<ImageDataUrlEdit>(p => p.Add(e => e.ValueChanged, v => value = v));

        edit.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(Png, "pixel.png", null, "image/png"));

        edit.WaitForAssertion(() => value.ShouldBe("data:image/png;base64," + Convert.ToBase64String(Png)));
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused()
    {
        string? value = null;
        Render<MudPopoverProvider>();
        var edit = Render<ImageDataUrlEdit>(p => p.Add(e => e.ValueChanged, v => value = v).Add(e => e.MaxBytes, 10));

        edit.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(Png, "pixel.png", null, "image/png"));

        await Task.Delay(500);
        value.ShouldBeNull();
    }
}