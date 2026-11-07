using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;

namespace Coworkee.Studio.ViewModels;

/// <summary>The process of an entity: initial state, transitions and what happens in a state (color, time stamp, notification, approval).</summary>
public sealed partial class WorkflowViewModel : ChangingViewModel
{
    public WorkflowViewModel(Action changed) : base(changed)
    {
        Transitions.CollectionChanged += (_, _) => changed();
        States.CollectionChanged += (_, _) => changed();
    }

    public static IReadOnlyList<string> Colors { get; } = ["", "primary", "secondary", "info", "success", "warning", "error"];

    [ObservableProperty]
    public partial string Initial { get; set; } = "Draft";

    [ObservableProperty]
    public partial string? MirrorTo { get; set; }

    public ObservableCollection<TransitionViewModel> Transitions { get; } = [];

    public ObservableCollection<StateViewModel> States { get; } = [];

    [RelayCommand]
    public void AddTransition() => Transitions.Add(new TransitionViewModel(Changed) { From = Transitions.LastOrDefault()?.To ?? Initial });

    [RelayCommand]
    public void RemoveTransition(TransitionViewModel transition) => Transitions.Remove(transition);

    [RelayCommand]
    public void AddState() => States.Add(new StateViewModel(Changed));

    [RelayCommand]
    public void RemoveState(StateViewModel state) => States.Remove(state);

    public WorkflowSpec ToSpec() => new()
    {
        Initial = Initial.Trim(),
        MirrorTo = Blank(MirrorTo),
        Transitions = [.. Transitions.Select(t => t.ToSpec())],
        States = [.. States.Select(s => s.ToSpec())],
    };

    public static WorkflowViewModel From(WorkflowSpec spec, Action changed)
    {
        var workflow = new WorkflowViewModel(changed) { Initial = spec.Initial, MirrorTo = spec.MirrorTo };
        foreach (var transition in spec.Transitions)
        {
            workflow.Transitions.Add(new TransitionViewModel(changed)
            {
                Name = transition.Name, From = string.Join(", ", transition.From), To = transition.To, Permission = transition.Permission, Comment = transition.Comment,
            });
        }

        foreach (var state in spec.States)
        {
            workflow.States.Add(StateViewModel.From(state, changed));
        }

        return workflow;
    }

    internal static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    internal static List<string> List(string? text) => [.. (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}

public sealed partial class TransitionViewModel(Action changed) : ChangingViewModel(changed)
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>The states it starts from, separated by commas.</summary>
    [ObservableProperty]
    public partial string From { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string To { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Permission { get; set; }

    [ObservableProperty]
    public partial bool Comment { get; set; }

    public WorkflowTransitionSpec ToSpec() =>
        new() { Name = Name.Trim(), From = WorkflowViewModel.List(From), To = To.Trim(), Permission = WorkflowViewModel.Blank(Permission), Comment = Comment };
}

public sealed partial class StateViewModel(Action changed) : ChangingViewModel(changed)
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Color { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Stamp { get; set; }

    [ObservableProperty]
    public partial string? Notify { get; set; }

    [ObservableProperty]
    public partial bool HasApproval { get; set; }

    [ObservableProperty]
    public partial string Approved { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Rejected { get; set; } = string.Empty;

    /// <summary>The roles whose members decide, separated by commas.</summary>
    [ObservableProperty]
    public partial string Roles { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? DueInDays { get; set; }

    [ObservableProperty]
    public partial decimal? RemindAfterDays { get; set; }

    [ObservableProperty]
    public partial bool NotByRequester { get; set; }

    public WorkflowStateSpec ToSpec() => new()
    {
        Name = Name.Trim(),
        Color = WorkflowViewModel.Blank(Color),
        Stamp = WorkflowViewModel.Blank(Stamp),
        Notify = WorkflowViewModel.Blank(Notify),
        Approval = !HasApproval ? null : new ApprovalSpec
        {
            Approved = Approved.Trim(),
            Rejected = Rejected.Trim(),
            Roles = WorkflowViewModel.List(Roles),
            DueInDays = (int?)DueInDays,
            RemindAfterDays = (int?)RemindAfterDays,
            NotByRequester = NotByRequester,
        },
    };

    public static StateViewModel From(WorkflowStateSpec spec, Action changed) => new(changed)
    {
        Name = spec.Name,
        Color = spec.Color ?? string.Empty,
        Stamp = spec.Stamp,
        Notify = spec.Notify,
        HasApproval = spec.Approval is not null,
        Approved = spec.Approval?.Approved ?? string.Empty,
        Rejected = spec.Approval?.Rejected ?? string.Empty,
        Roles = string.Join(", ", spec.Approval?.Roles ?? []),
        DueInDays = spec.Approval?.DueInDays,
        RemindAfterDays = spec.Approval?.RemindAfterDays,
        NotByRequester = spec.Approval?.NotByRequester ?? false,
    };
}

/// <summary>Tells the wizard about every change of a property, so validation and the preview follow.</summary>
public abstract class ChangingViewModel(Action changed) : ObservableObject
{
    protected Action Changed { get; } = changed;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Changed();
    }
}
