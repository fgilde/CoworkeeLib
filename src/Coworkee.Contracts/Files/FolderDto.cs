namespace Coworkee.Contracts.Files;

public sealed record FolderDto(Guid Id, Guid? ParentId, string Name, DateTimeOffset CreatedAt, Guid? CreatedBy)
{
    /// <summary>The root folder of the registration documents; each user's documents sit in a folder of his own below it.</summary>
    public const string RegistrationsRoot = "Registrations";
}
