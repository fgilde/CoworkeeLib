namespace Coworkee.Studio.Cli;

public enum AppStatus
{
    Stopped,
    Running,

    /// <summary>No app host of this app runs, but something else listens on its dashboard port: a start would fail.</summary>
    PortTaken,
}
