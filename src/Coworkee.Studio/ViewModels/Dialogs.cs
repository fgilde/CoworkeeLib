namespace Coworkee.Studio.ViewModels;

/// <summary>What the view models need from the window: pickers, clipboard and opening things; the window fills it in, tests replace it.</summary>
public sealed class Dialogs
{
    /// <summary>Title and file patterns (*.json), returns a path or null.</summary>
    public Func<string, string[], Task<string?>> OpenFile { get; set; } = (_, _) => Task.FromResult<string?>(null);

    public Func<string, Task<string?>> PickFolder { get; set; } = _ => Task.FromResult<string?>(null);

    /// <summary>Suggested file name, returns a path or null.</summary>
    public Func<string, Task<string?>> SaveFile { get; set; } = _ => Task.FromResult<string?>(null);

    public Func<string, Task> Copy { get; set; } = _ => Task.CompletedTask;

    /// <summary>Folder, file or web address.</summary>
    public Action<string> Open { get; set; } = _ => { };
}
