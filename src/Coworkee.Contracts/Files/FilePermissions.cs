namespace Coworkee.Contracts.Files;

public static class FilePermissions
{
    public const string GroupName = "Files";
    public const string View = "Files.View";
    public const string Upload = "Files.Upload";
    public const string Manage = "Files.Manage";

    /// <summary>Opens the registration documents of all users; global file grants do not reach them.</summary>
    public const string ViewRegistrations = "Files.Registrations.View";

    /// <summary>Resource type of folders for per-folder grants; a grant on a folder covers its subfolders.</summary>
    public const string FolderResource = "FileFolder";
}
