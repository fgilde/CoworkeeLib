namespace Coworkee.Application.Privacy;

/// <summary>The user whose data is exported or erased; read before any contributor runs, so the order of the contributors does not matter.</summary>
public sealed record PersonalDataSubject(Guid UserId, Guid TenantId, string? Email)
{
    /// <summary>Takes the erased user's place in records that stay (author, owner, approver and actor columns): "a user who was erased", unlike null, "nobody".</summary>
    public static readonly Guid ErasedUserId = Guid.Empty;
}
