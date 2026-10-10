using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

internal sealed class FilePickerSettingDefinitions : ISettingDefinitionContributor
{
    private static readonly SettingScope[] Global = [SettingScope.Global];

    public void Define(SettingDefinitionContext context) =>
        context.Group("FilePickers", "Cloud file pickers")
            .Add(FilePickerSettings.GoogleDriveEnabled, "Google Drive", SettingType.Bool, Global, "true", visibleToClient: true)
            .Add(FilePickerSettings.GoogleDriveClientId, "Google Drive client id", SettingType.String, Global, visibleToClient: true,
                description: "OAuth client id of type web application from the Google Cloud console.")
            .Add(FilePickerSettings.OneDriveEnabled, "OneDrive", SettingType.Bool, Global, "true", visibleToClient: true)
            .Add(FilePickerSettings.OneDriveClientId, "OneDrive client id", SettingType.String, Global, visibleToClient: true,
                description: "Application (client) id of the app registration in Microsoft Entra.")
            .Add(FilePickerSettings.DropboxEnabled, "Dropbox", SettingType.Bool, Global, "true", visibleToClient: true)
            .Add(FilePickerSettings.DropboxAppKey, "Dropbox app key", SettingType.String, Global, visibleToClient: true,
                description: "App key of the Dropbox app with the Chooser.");
}
