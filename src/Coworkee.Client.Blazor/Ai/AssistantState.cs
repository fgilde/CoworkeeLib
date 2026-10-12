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

    public event Action? Changed;

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
