using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Cli;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public enum RunState
{
    Ready,
    Running,
    Succeeded,
    Failed,
    Invalid,
    CliMissing,
    Canceled,
}

public enum ProgressState
{
    Running,
    Done,
    Failed,
}

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

public sealed partial class GenerateStep(MainViewModel main) : StepViewModel(main)
{
    private readonly Stopwatch _watch = new();
    private CancellationTokenSource? _cancel;
    private bool _nextSteps;

    protected override string TitleText => "Create";

    protected override string SubtitleText => "The CLI installs the template, creates the solution, generates your modules and restores the packages.";

    protected override string PitchText => "This takes a minute or two. Afterwards: open the folder, open it in your IDE, or run the app with the Aspire dashboard right away.";

    public override string Icon => "Icon.Flag";

    /// <summary>Runs an update of the UI on its thread; tests run it in place.</summary>
    public Action<Action> Dispatch { get; set; } = action => Dispatcher.UIThread.Post(action);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsReady), nameof(IsSucceeded), nameof(IsFailed), nameof(IsCliMissing), nameof(IsFinished), nameof(Headline))]
    public partial RunState RunState { get; set; }

    [ObservableProperty]
    public partial bool ShowLog { get; set; }

    [ObservableProperty]
    public partial string Elapsed { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Output { get; set; }

    public ObservableCollection<ProgressStepViewModel> Progress { get; } = [];

    public ObservableCollection<CliLine> Log { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public ObservableCollection<string> NextSteps { get; } = [];

    /// <summary>The users of the created app with their passwords, read from its DemoUsers.cs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSignIn), nameof(SignInHint), nameof(SignInText))]
    public partial SignInInfo? SignIn { get; set; }

    public bool HasSignIn => SignIn is { Users.Count: > 0 };

    public string SignInHint => SignIn is { } info ? L.Format("Run the app, open {0} in the Aspire dashboard and sign in with:", info.WebResource) : string.Empty;

    /// <summary>All users with passwords, for the clipboard.</summary>
    public string SignInText => SignIn is { } info ? string.Join(Environment.NewLine, info.Users.Select(u => $"{u.Email}\t{u.Password}\t{u.RoleText}")) : string.Empty;

    public bool IsRunning => RunState == RunState.Running;

    public bool IsReady => RunState == RunState.Ready;

    public bool IsSucceeded => RunState == RunState.Succeeded;

    public bool IsFailed => RunState is RunState.Failed or RunState.Invalid or RunState.Canceled;

    public bool IsCliMissing => RunState == RunState.CliMissing;

    public bool IsFinished => RunState is not (RunState.Ready or RunState.Running);

    public string Headline => L.Get(RunState switch
    {
        RunState.Running => "Creating your app",
        RunState.Succeeded => "Your app is ready",
        RunState.Invalid => "The spec has problems",
        RunState.CliMissing => "The coworkee CLI was not found",
        RunState.Canceled => "Canceled",
        RunState.Failed => "Something went wrong",
        _ => "Ready to create",
    });

    public override bool CanContinue => false;

    [RelayCommand]
    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        Reset();
        if (Main.HasErrors)
        {
            foreach (var error in Main.Errors)
            {
                Log.Add(new CliLine(CliLineKind.Error, error));
            }

            RunState = RunState.Invalid;
            ShowLog = true;
            return;
        }

        var spec = Main.ToSpec();
        Output = spec.Output;
        RunState = RunState.Running;
        Main.SaveSettings();
        Main.UpdateStates();
        _cancel = new CancellationTokenSource();
        _watch.Restart();
        using var timer = new Timer(_ => Dispatch(() => Elapsed = _watch.Elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture)), null, 0, 1000);
        var outcome = await Main.Generator.RunAsync(spec, Main.Settings, line => Dispatch(() => Add(line)), _cancel.Token);
        _watch.Stop();
        Dispatch(() => Finish(outcome));
    }

    [RelayCommand]
    public void Cancel() => _cancel?.Cancel();

    [RelayCommand]
    public async Task InstallCliAsync()
    {
        RunState = RunState.Running;
        ShowLog = true;
        var installed = await Main.Generator.InstallCliAsync(Main.Settings, line => Dispatch(() => Add(line)), CancellationToken.None);
        Dispatch(() =>
        {
            RunState = installed ? RunState.Ready : RunState.CliMissing;
            if (installed)
            {
                StartCommand.Execute(null);
            }
        });
    }

    [RelayCommand]
    public async Task ChooseCliAsync()
    {
        await Main.BrowseCliCommand.ExecuteAsync(null);
        if (Main.CliPath is not null)
        {
            await StartAsync();
        }
    }

    [RelayCommand]
    public void OpenFolder()
    {
        if (Output is { } output)
        {
            Main.Dialogs.Open(output);
        }
    }

    [RelayCommand]
    public void OpenSolution()
    {
        if (Solution() is { } solution)
        {
            Main.Dialogs.Open(solution);
        }
    }

    [RelayCommand]
    public void OpenIn(string tool)
    {
        if (Output is { } output)
        {
            TryLaunch(() => Launch.Tool(tool, tool == "rider" ? Solution() ?? output : output, output));
        }
    }

    [RelayCommand]
    public void RunApp()
    {
        if (Output is { } output)
        {
            var cli = CliInvocation.Resolve(Main.Settings.CliPath);
            TryLaunch(() => Launch.Terminal(CommandLine.Format([cli.FileName, .. cli.Prefix, "run"]), output));
        }
    }

    [RelayCommand]
    public Task CopyLogAsync() => Main.CopyAsync(string.Join(Environment.NewLine, Log.Select(l => l.Kind switch
    {
        CliLineKind.Step => "==> " + l.Text,
        CliLineKind.Command => "> " + l.Text,
        CliLineKind.Warning => "warn  " + l.Text,
        _ => l.Text,
    })));

    public void Reset()
    {
        Progress.Clear();
        Log.Clear();
        Warnings.Clear();
        NextSteps.Clear();
        SignIn = null;
        _nextSteps = false;
        Elapsed = string.Empty;
        RunState = RunState.Ready;
    }

    public void Add(CliLine line)
    {
        Log.Add(line);
        switch (line.Kind)
        {
            case CliLineKind.Step:
                foreach (var running in Progress.Where(p => p.IsRunning))
                {
                    running.State = ProgressState.Done;
                }

                Progress.Add(new ProgressStepViewModel(line.Text));
                break;
            case CliLineKind.Warning:
                Warnings.Add(line.Text);
                break;
            case CliLineKind.NextSteps:
                _nextSteps = true;
                break;
            case CliLineKind.Text when _nextSteps && line.Text.Trim().Length > 0:
                NextSteps.Add(line.Text.Trim());
                break;
        }
    }

    public void Finish(GenerationOutcome outcome)
    {
        foreach (var running in Progress.Where(p => p.IsRunning))
        {
            running.State = outcome == GenerationOutcome.Succeeded ? ProgressState.Done : ProgressState.Failed;
        }

        RunState = outcome switch
        {
            GenerationOutcome.Succeeded => RunState.Succeeded,
            GenerationOutcome.Invalid => RunState.Invalid,
            GenerationOutcome.CliNotFound => RunState.CliMissing,
            GenerationOutcome.Canceled => RunState.Canceled,
            _ => RunState.Failed,
        };
        ShowLog |= RunState is RunState.Failed or RunState.Invalid;
        SignIn = RunState == RunState.Succeeded && Output is { } output ? SignInInfo.Read(output) : null;
        Main.UpdateStates();
    }

    private string? Solution() => Output is { } output && Directory.Exists(output) ? Directory.GetFiles(output, "*.slnx").FirstOrDefault() : null;

    private void TryLaunch(Action launch)
    {
        try
        {
            launch();
        }
        catch (Win32Exception exception)
        {
            Main.Status = exception.Message;
        }
    }
}
