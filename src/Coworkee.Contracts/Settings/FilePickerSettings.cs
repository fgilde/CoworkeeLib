namespace Coworkee.Contracts.Settings;

/// <summary>The cloud pickers of every upload field; a picker shows when it is enabled and has its client id or app key.</summary>
public static class FilePickerSettings
{
    public const string GoogleDriveEnabled = "FilePickers.GoogleDrive.Enabled";
    public const string GoogleDriveClientId = "FilePickers.GoogleDrive.ClientId";
    public const string OneDriveEnabled = "FilePickers.OneDrive.Enabled";
    public const string OneDriveClientId = "FilePickers.OneDrive.ClientId";
    public const string DropboxEnabled = "FilePickers.Dropbox.Enabled";
    public const string DropboxAppKey = "FilePickers.Dropbox.AppKey";
}
