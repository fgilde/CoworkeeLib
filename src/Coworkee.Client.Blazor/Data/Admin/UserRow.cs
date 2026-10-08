namespace Coworkee.Client.Blazor.Data.Admin;

/// <summary>A row of the OData set "Users".</summary>
public sealed record UserRow(Guid Id, string Email, string? FirstName, string? LastName, bool IsActive, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt)
{
    public string Name => $"{FirstName} {LastName}".Trim();
}
