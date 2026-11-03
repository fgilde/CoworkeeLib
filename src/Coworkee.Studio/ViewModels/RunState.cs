namespace Coworkee.Studio.ViewModels;

public enum RunState
{
    Ready,
    Running,

    /// <summary>The plan of the changes to an existing app is shown; Apply carries it out.</summary>
    PlanReady,
    Succeeded,
    Failed,
    Invalid,
    CliMissing,
    Canceled,
}
