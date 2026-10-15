using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Coworkee.Studio.Cli;

public enum AppStatus
{
    Stopped,
    Running,

    /// <summary>No app host of this app runs, but something else listens on its dashboard port: a start would fail.</summary>
    PortTaken,
}

/// <summary>Whether an app runs outside the Studio: its app host process (<c>Shop.AppHost</c>) or the dashboard port of its launch settings.</summary>
public sealed class AppProbe
{
    public Func<string, bool> HasProcess { get; init; } = name => Each(name, _ => { }) > 0;

    public Func<int, bool> IsListening { get; init; } = port => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port);

    /// <summary>Ends every process of that name with its children (the app host takes its services along).</summary>
    public Action<string> Kill { get; init; } = name => Each(name, p => p.Kill(entireProcessTree: true));

    public static string? ProcessName(string folder) => AspireOutput.AppHost(folder) is { } project ? Path.GetFileNameWithoutExtension(project) : null;

    public AppStatus Status(string folder)
    {
        if (ProcessName(folder) is not { } name)
        {
            return AppStatus.Stopped;
        }

        if (HasProcess(name))
        {
            return AppStatus.Running;
        }

        return Uri.TryCreate(AspireOutput.Dashboard(folder), UriKind.Absolute, out var dashboard) && IsListening(dashboard.Port) ? AppStatus.PortTaken : AppStatus.Stopped;
    }

    private static int Each(string name, Action<Process> action)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes)
        {
            try
            {
                action(process);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // ended meanwhile or not ours
            }
            finally
            {
                process.Dispose();
            }
        }

        return processes.Length;
    }
}
