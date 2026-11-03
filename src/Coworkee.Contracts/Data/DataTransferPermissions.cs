namespace Coworkee.Contracts.Data;

public static class DataTransferPermissions
{
    public const string GroupName = "DataTransfer";

    /// <summary>Opens the import and export page; each set still needs its own permissions.</summary>
    public const string Use = "DataTransfer.Use";

    /// <summary>Recurring list exports by mail and exports mailed to other addresses; the set still needs its own permissions.</summary>
    public const string ExportSchedules = "DataTransfer.ExportSchedules";
}
