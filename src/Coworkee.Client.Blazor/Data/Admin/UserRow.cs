namespace Coworkee.Client.Blazor.Data.Admin;

/// <summary>A row of the OData set "Users".</summary>
public sealed record UserRow(Guid Id, string Email, string? FirstName, string? LastName, bool IsActive, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt,
    DateTimeOffset? LockoutEnd = null)
{
    public string Name => $"{FirstName} {LastName}".Trim();

    /// <summary>Locked by an administrator or after too many failed sign-ins.</summary>
    public bool IsLocked => LockoutEnd > DateTimeOffset.UtcNow;
}
