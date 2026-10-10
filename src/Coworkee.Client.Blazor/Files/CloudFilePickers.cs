using Coworkee.Contracts.Settings;
using MudBlazor.Extensions.Options;

namespace Coworkee.Client.Blazor.Files;

/// <summary>Hands the cloud picker ids of the client settings to MudEx, so every <c>MudExUploadEdit</c> offers the configured pickers.</summary>
public static class CloudFilePickers
{
    public static void Apply(MudExConfiguration configuration, IReadOnlyDictionary<string, string?> settings) =>
        configuration
            .EnableGoogleDriveIntegration(Id(settings, FilePickerSettings.GoogleDriveEnabled, FilePickerSettings.GoogleDriveClientId))
            .EnableOneDriveIntegration(Id(settings, FilePickerSettings.OneDriveEnabled, FilePickerSettings.OneDriveClientId))
            .EnableDropBoxIntegration(Id(settings, FilePickerSettings.DropboxEnabled, FilePickerSettings.DropboxAppKey));

    private static string? Id(IReadOnlyDictionary<string, string?> settings, string enabled, string id) =>
        bool.TryParse(settings.GetValueOrDefault(enabled), out var on) && !on ? null
            : settings.GetValueOrDefault(id) is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
