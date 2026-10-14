using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;

namespace Coworkee.Mailing;

public sealed record RecipientPage(IReadOnlyList<MailRecipient> Recipients, Guid? Next);

/// <summary>Resolves a recipient source to the eligible users of the current tenant; implemented where the users live.</summary>
public interface IMailRecipientDirectory
{
    /// <summary>The next <paramref name="size"/> eligible users after <paramref name="after"/>, by user id; <see cref="RecipientPage.Next"/> is the cursor of the following page, null at the end.</summary>
    Task<Result<RecipientPage>> PageAsync(RecipientSourceDto source, Guid? after, int size, CancellationToken cancellationToken);

    Task<Result<RecipientPreviewDto>> PreviewAsync(RecipientSourceDto source, int first, CancellationToken cancellationToken);
}
