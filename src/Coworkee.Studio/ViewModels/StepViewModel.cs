using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public enum StepState
{
    Upcoming,
    Current,
    Done,
}

/// <summary>A page of the wizard; titles are English keys of <see cref="L"/>.</summary>
public abstract partial class StepViewModel(MainViewModel main) : ObservableObject
{
    public MainViewModel Main => main;

    protected abstract string TitleText { get; }

    protected abstract string SubtitleText { get; }

    protected abstract string PitchText { get; }

    public abstract string Icon { get; }

    public string Title => L.Get(TitleText);

    public string Subtitle => L.Get(SubtitleText);

    public string Pitch => L.Get(PitchText);

    public int Number => main.Steps.ToList().IndexOf(this) + 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial IReadOnlyList<string> Errors { get; set; } = [];

    public bool HasErrors => Errors.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrent), nameof(IsDone))]
    public partial StepState State { get; set; }

    [ObservableProperty]
    public partial bool IsReachable { get; set; }

    public bool IsCurrent => State == StepState.Current;

    public bool IsDone => State == StepState.Done;

    /// <summary>Validation messages of the spec ("path: message") that belong on this page.</summary>
    public virtual bool Owns(string error) => false;

    /// <summary>Next is possible; the steps after it need what this one asks for.</summary>
    public virtual bool CanContinue => true;

    public virtual void OnShown()
    {
    }

    /// <summary>The spec changed somewhere.</summary>
    public virtual void Refresh()
    {
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(string.Empty);
    }
}
