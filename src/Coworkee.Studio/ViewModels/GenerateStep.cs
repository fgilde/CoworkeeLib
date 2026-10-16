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

    /// <summary>The plan of the changes to an existing app is shown; Apply carries it out.</summary>
    PlanReady,
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

public sealed partial class GenerateStep : StepViewModel
{
    private readonly Stopwatch _watch = new();
    private CancellationTokenSource? _cancel;
    private bool _nextSteps;

    public GenerateStep(MainViewModel main)
        : base(main)
    {
        App = new AppRunViewModel(main, () => Output, action => Dispatch(action));
        Deploy = new DeployViewModel(main, () => Output, action => Dispatch(action));
    }

    protected override string TitleText => "Create";

    protected override string SubtitleText => "The CLI installs the template, creates the solution, generates your modules and restores the packages.";

    protected override string PitchText => "This takes a minute or two. Afterwards: open the folder, open it in your IDE, or run the app with the Aspire dashboard right away.";

    public override string Icon => "Icon.Flag";

    /// <summary>Runs an update of the UI on its thread; tests run it in place.</summary>
    public Action<Action> Dispatch { get; set; } = action => Dispatcher.UIThread.Post(action);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsReady), nameof(IsPlanReady), nameof(IsSucceeded), nameof(IsFailed), nameof(IsCliMissing), nameof(IsFinished), nameof(Headline), nameof(StartText))]
    public partial RunState RunState { get; set; }

    [ObservableProperty]
    public partial bool ShowLog { get; set; }

    [ObservableProperty]
    public partial string Elapsed { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Output { get; set; }

    /// <summary>The created app, run from the Studio.</summary>
    public AppRunViewModel App { get; }

    public DeployViewModel Deploy { get; }

    public ObservableCollection<ProgressStepViewModel> Progress { get; } = [];

    public ObservableCollection<CliLine> Log { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public ObservableCollection<string> NextSteps { get; } = [];

    /// <summary>The .rej files coworkee add wrote next to files whose changes it could not merge.</summary>
    public ObservableCollection<string> Conflicts { get; } = [];

    /// <summary>The users of the created app with their passwords, read from its DemoUsers.cs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSignIn), nameof(SignInHint), nameof(SignInText), nameof(ToolsHint))]
    public partial SignInInfo? SignIn { get; set; }

    public bool HasSignIn => SignIn is { Users.Count: > 0 };

    public string SignInHint => SignIn is { } info ? L.Format("Run the app, open {0} in the Aspire dashboard and sign in with:", info.WebResource) : string.Empty;

    /// <summary>All users with passwords, for the clipboard.</summary>
    public string ToolsHint => SignIn is { Tools.Count: > 0, Administrator: { } admin } info
        ? L.Format(info.Keycloak ? "{0} in the Aspire dashboard take {1} and the Keycloak button too." : "{0} in the Aspire dashboard take {1} too.", string.Join(", ", info.Tools), admin.Email)
        : string.Empty;

    public string SignInText => SignIn is { } info ? string.Join(Environment.NewLine, info.Users.Select(u => $"{u.Email}\t{u.Password}\t{u.RoleText}")) : string.Empty;

    public bool IsRunning => RunState == RunState.Running;

    public bool IsReady => RunState == RunState.Ready;

    public bool IsPlanReady => RunState == RunState.PlanReady;

    public bool HasConflicts => Conflicts.Count > 0;

    public string StartText => L.Get(IsFinished ? "Run again" : Main.IsExistingApp ? "Show the changes" : "Create the app");

    public bool IsSucceeded => RunState == RunState.Succeeded;

    public bool IsFailed => RunState is RunState.Failed or RunState.Invalid or RunState.Canceled;

    public bool IsCliMissing => RunState == RunState.CliMissing;

    public bool IsFinished => RunState is not (RunState.Ready or RunState.Running);

    public string Headline => L.Get(RunState switch
    {
        RunState.Running => Main.IsExistingApp ? "Updating your app" : "Creating your app",
        RunState.PlanReady => "Check the plan, then apply it",
        RunState.Succeeded => Main.IsExistingApp ? "Your app is updated" : "Your app is ready",
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
        Output = Main.ExistingApp ?? spec.Output;
        if (Main.ExistingApp is { } app)
        {
            await RunAddAsync(app, spec, dryRun: true);
            return;
        }

        RunState = RunState.Running;
        Main.SaveSettings();
        Main.UpdateStates();
        _cancel = new CancellationTokenSource();
        _watch.Restart();
        using var timer = new Timer(_ => Dispatch(() => Elapsed = _watch.Elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture)), null, 0, 1000);
        var outcome = await Main.Generator.RunAsync(spec, Main.Settings, line => Dispatch(() => Add(line)), _cancel.Token);
        if (outcome == GenerationOutcome.Succeeded && Main.AiApiKey is { } key && Main.Option("ai").IsOn && spec.Output is { } output)
        {
            await StoreAiKeyAsync(Path.Combine(output, "src", spec.Name + ".AppHost"), key);
        }

        _watch.Stop();
        Dispatch(() => Finish(outcome));
    }

    /// <summary>Carries out the plan shown after the dry run.</summary>
    [RelayCommand]
    public async Task ApplyAsync()
    {
        if (IsPlanReady && Main.ExistingApp is { } app)
        {
            var spec = Main.ToSpec();
            Reset();
            await RunAddAsync(app, spec, dryRun: false);
        }
    }

    [RelayCommand]
    public void Cancel() => _cancel?.Cancel();

    private async Task RunAddAsync(string app, AppSpec spec, bool dryRun)
    {
        RunState = RunState.Running;
        Main.UpdateStates();
        _cancel = new CancellationTokenSource();
        _watch.Restart();
        using var timer = new Timer(_ => Dispatch(() => Elapsed = _watch.Elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture)), null, 0, 1000);
        spec.Output = null;
        var outcome = await Main.Generator.ApplyAsync(app, spec, dryRun, Main.Settings, line => Dispatch(() => Add(line)), _cancel.Token);
        _watch.Stop();
        Dispatch(() =>
        {
            Finish(outcome);
            if (dryRun && outcome == GenerationOutcome.Succeeded)
            {
                RunState = RunState.PlanReady;
                ShowLog = true;
            }
        });
    }

    private async Task StoreAiKeyAsync(string appHost, string key)
    {
        Dispatch(() => Add(new CliLine(CliLineKind.Step, L.Get("Storing the AI key in the user secrets of the app host"))));
        if (!await Main.Generator.StoreSecretAsync(appHost, "Parameters:ai-apikey", key, line => Dispatch(() => Add(line)), CancellationToken.None))
        {
            Dispatch(() => Add(new CliLine(CliLineKind.Warning,
                L.Format("The AI key could not be stored; run: dotnet user-secrets set Parameters:ai-apikey <key> --project {0}", appHost))));
        }
    }

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
        Conflicts.Clear();
        OnPropertyChanged(nameof(HasConflicts));
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
            case CliLineKind.Text when line.Text.StartsWith("conflict ", StringComparison.Ordinal):
                Conflicts.Add(line.Text["conflict ".Length..].Trim());
                OnPropertyChanged(nameof(HasConflicts));
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
        ShowLog |= Conflicts.Count > 0;
        if (RunState == RunState.Succeeded && Output is { } created)
        {
            Main.Apps.Remember(created, Main.ToSpec());
        }

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
