namespace Coworkee.Client.Blazor.Ai;

public enum AssistantSize
{
    Small,
    Large,
    Full,
}

/// <summary>Whether the assistant window is open and how big, shared by the button, the shortcut and /assistant.</summary>
public sealed class AssistantState
{
    public bool IsOpen { get; private set; }

    public AssistantSize Size { get; private set; } = AssistantSize.Small;

    /// <summary>Text the window puts into its input when it shows, e.g. a question from the command palette.</summary>
    public string? Draft { get; private set; }

    public event Action? Changed;

    public void Ask(string text)
    {
        Draft = text;
        Open();
    }

    public string? TakeDraft()
    {
        var draft = Draft;
        Draft = null;
        return draft;
    }

    public void Open(AssistantSize? size = null)
    {
        IsOpen = true;
        Size = size ?? Size;
        Changed?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        Changed?.Invoke();
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Resize(AssistantSize size) => Open(size);
}
