namespace Coworkee.Mailing;

public interface IMailUnsubscribeLinks
{
    /// <summary>The link that opts the user out of newsletters; absolute from the public app address, else from the current request; throws when neither exists.</summary>
    string Create(Guid userId, Guid? tenantId);

    bool TryRead(string? token, out Guid userId, out Guid? tenantId);
}
