using Coworkee.Contracts.Mailing;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>The latest rendering of the template being edited; the open preview sheet follows it.</summary>
public sealed class MailPreview
{
    public RenderedMailDto? Rendered { get; private set; }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public event Action? Changed;

    public void Show(RenderedMailDto? rendered, IReadOnlyList<string> errors)
    {
        Rendered = rendered ?? Rendered;
        Errors = errors;
        Changed?.Invoke();
    }
}
