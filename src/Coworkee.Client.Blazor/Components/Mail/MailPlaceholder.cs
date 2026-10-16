namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>A placeholder as mention of the visual mail editor; both editors show its <see cref="ToString"/>.</summary>
public sealed record MailPlaceholder(string Name)
{
    /// <summary>The <c>data-__type</c> of its mention spans; TipTap only reads spans that carry it back as mentions.</summary>
    public static readonly string MentionType = typeof(MailPlaceholder).FullName!;

    public override string ToString() => Name;
}
