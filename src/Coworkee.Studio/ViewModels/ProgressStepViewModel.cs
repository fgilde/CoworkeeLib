using CommunityToolkit.Mvvm.ComponentModel;

namespace Coworkee.Studio.ViewModels;

public sealed partial class ProgressStepViewModel(string text) : ObservableObject
{
    public string Text => text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsDone), nameof(IsFailed))]
    public partial ProgressState State { get; set; }

    public bool IsRunning => State == ProgressState.Running;

    public bool IsDone => State == ProgressState.Done;

    public bool IsFailed => State == ProgressState.Failed;
}
