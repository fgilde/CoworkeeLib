using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Cli;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public enum AzureStep
{
    SignIn,
    Subscription,
    Target,
    Summary,
    Deploy,
}

public enum AzureSignIn
{
    Checking,
    NotInstalled,
    SignedOut,

    /// <summary>az account show answers, but the Windows broker (WAM) keeps the token where az get-access-token does not look.</summary>
    BrokerProblem,
    SignedIn,
}

/// <summary>A subscription the Azure CLI sees, with the account (<c>user.name</c>) it is signed in with; several accounts can be signed in at once.</summary>
public sealed record AzureSubscription(string Id, string Name, string TenantId, bool IsDefault, string User = "");

/// <summary>The subscriptions of one signed-in account in one tenant, as the wizard groups them.</summary>
public sealed record AzureAccount(string User, string TenantId, IReadOnlyList<AzureSubscriptionPick> Subscriptions);

public sealed record AzureSubscriptionPick(AzureSubscription Subscription, bool IsPicked);

public sealed record AzureResourceGroup(string Name, string Location);

/// <summary>A tool of the app host in the deployment: on is the default, off passes --without, Mailpit on passes --with.</summary>
public sealed partial class ToolSwitch(string name, bool isOn) : ObservableObject
{
    public string Name { get; } = name;

    public bool Default { get; } = Deployments.DeployedByDefault(name);

    [ObservableProperty]
    public partial bool IsOn { get; set; } = isOn;
}

/// <summary>
/// The deploy wizard: Azure CLI installed and signed in, subscription, resource group and region, a summary, then coworkee deploy azure --apply
/// with its output live. Every az call goes through the process runner of the Studio; nothing is created before the Deploy button.
/// </summary>
public sealed partial class AzureDeployViewModel : ObservableObject
{
    public const string InstallUrl = "https://learn.microsoft.com/cli/azure/install-azure-cli";

    /// <summary>The fix for the Windows broker problem, as the wizard shows it.</summary>
    public const string BrokerFixCommands = "az config set core.enable_broker_on_windows=false\naz login";

    private readonly DeploymentRecord? _update;

    public AzureDeployViewModel(AppOverviewViewModel overview, DeploymentRecord? update = null)
    {
        Overview = overview;
        _update = update;
        Run = new DeployViewModel(overview.Main, () => overview.App.Path, action => overview.Main.Apps.Dispatch(action));
        Run.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DeployViewModel.IsBusy) or nameof(DeployViewModel.ExitCode))
            {
                OnPropertyChanged(nameof(DeployStatus));
                OnPropertyChanged(nameof(IsFailed));
            }
        };
        Tools = [.. Deployments.Tools.Select(t => new ToolSwitch(t, update is not null && update.Tools.TryGetValue(t, out var on) ? on : Deployments.DeployedByDefault(t)))];
        if (update is not null)
        {
            ResourceGroup = update.ResourceGroup;
            Location = update.Location;
            Name = update.Name;
            OwnSignIn = update.OwnAzureSignIn;
        }
    }

    public static IReadOnlyList<string> DefaultRegions { get; } =
        ["germanywestcentral", "westeurope", "northeurope", "swedencentral", "francecentral", "switzerlandnorth", "uksouth", "eastus", "eastus2", "westus2"];

    public AppOverviewViewModel Overview { get; }

    public MainViewModel Main => Overview.Main;

    /// <summary>The run of coworkee deploy: its log, busy state and exit code, as on the Create page.</summary>
    public DeployViewModel Run { get; }

    public bool IsUpdate => _update is not null;

    public string Title => IsUpdate ? L.Format("Update {0}", Name) : L.Format("Deploy {0}", Overview.App.Name);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignInStep), nameof(IsSubscriptionStep), nameof(IsTargetStep), nameof(IsSummaryStep), nameof(IsDeployStep), nameof(ShowSummary),
        nameof(StepText), nameof(CanGoNext), nameof(ShowNext))]
    public partial AzureStep Step { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChecking), nameof(IsNotInstalled), nameof(IsSignedOut), nameof(IsBrokerProblem), nameof(IsSignedIn), nameof(CanGoNext))]
    public partial AzureSignIn SignIn { get; set; }

    /// <summary>The account the Azure CLI is signed in with; follows the picked subscription.</summary>
    [ObservableProperty]
    public partial string? Account { get; set; }

    /// <summary>What the last failing az call printed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand), nameof(FixBrokerCommand), nameof(CheckCommand))]
    public partial bool IsWorking { get; set; }

    /// <summary>The tenant (id or domain) the next sign-in goes to, for guest access to another tenant; empty: the account's home tenant.</summary>
    [ObservableProperty]
    public partial string SignInTenant { get; set; } = string.Empty;

    /// <summary>
    /// The app signs in to Azure on its own: every az call and coworkee deploy get <c>AZURE_CONFIG_DIR</c> = <see cref="ConfigDir"/>, the global sign-in of
    /// the Azure CLI stays untouched.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignInText))]
    public partial bool OwnSignIn { get; set; }

    /// <summary>The app's own Azure CLI folder under the user profile, never in the repository: it holds tokens.</summary>
    public string ConfigDir => Deployments.AzureConfigDir(Overview.App.Path);

    public string ConfigDirText => L.Format("The app's own sign-in is kept in {0}; the global sign-in of the Azure CLI stays as it is.", ConfigDir);

    public string SignInText => L.Get(OwnSignIn ? "own sign-in of this app" : "the Azure CLI's sign-in");

    public ObservableCollection<AzureSubscription> Subscriptions { get; } = [];

    /// <summary>The subscriptions grouped by account and tenant, in the order the Azure CLI lists them.</summary>
    public IReadOnlyList<AzureAccount> Accounts =>
    [
        .. Subscriptions.GroupBy(s => (s.User, s.TenantId))
            .Select(g => new AzureAccount(g.Key.User, g.Key.TenantId, [.. g.Select(s => new AzureSubscriptionPick(s, s == Subscription))])),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext), nameof(Accounts))]
    public partial AzureSubscription? Subscription { get; set; }

    public ObservableCollection<AzureResourceGroup> Groups { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewGroup), nameof(GroupError), nameof(CanGoNext), nameof(ScopeText))]
    public partial string ResourceGroup { get; set; } = string.Empty;

    public ObservableCollection<string> Regions { get; } = [.. DefaultRegions];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial string? Location { get; set; } = DefaultRegions[0];

    /// <summary>The name of the deployment record; follows the resource group until it is changed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError), nameof(CanGoNext))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AdminEmail { get; set; } = string.Empty;

    public IReadOnlyList<ToolSwitch> Tools { get; }

    /// <summary>The record coworkee deploy wrote, once it succeeded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDone), nameof(Links), nameof(PasswordText))]
    public partial DeploymentRecord? Result { get; set; }

    public bool IsSignInStep => Step == AzureStep.SignIn;

    public bool IsSubscriptionStep => Step == AzureStep.Subscription;

    public bool IsTargetStep => Step == AzureStep.Target;

    public bool IsSummaryStep => Step == AzureStep.Summary;

    public bool IsDeployStep => Step == AzureStep.Deploy;

    public bool ShowSummary => Step >= AzureStep.Summary;

    public bool ShowNext => Step < AzureStep.Summary;

    public bool IsChecking => SignIn == AzureSignIn.Checking;

    public bool IsNotInstalled => SignIn == AzureSignIn.NotInstalled;

    public bool IsSignedOut => SignIn == AzureSignIn.SignedOut;

    public bool IsBrokerProblem => SignIn == AzureSignIn.BrokerProblem;

    public bool IsSignedIn => SignIn == AzureSignIn.SignedIn;

    public bool HasProblem => Problem is { Length: > 0 };

    public string StepText => L.Format("Step {0} of {1}", (int)(Step > AzureStep.Summary ? AzureStep.Summary : Step) + 1, 4);

    public bool IsNewGroup => !Groups.Any(g => string.Equals(g.Name, ResourceGroup.Trim(), StringComparison.OrdinalIgnoreCase));

    public string? GroupError => ResourceGroup.Trim() is { Length: > 0 } group && !GroupName().IsMatch(group)
        ? L.Get("Up to 90 letters, digits, - _ . ( ); not ending with a dot.")
        : null;

    public string? NameError => Name.Trim() is { Length: > 0 } name && !RecordName().IsMatch(name) ? L.Get("Letters, digits, - _ and . only.") : null;

    public string ScopeText => L.Format("Everything is created in resource group {0} only; nothing else in the subscription is touched.", ResourceGroup.Trim());

    public string GroupText => IsNewGroup ? L.Format("{0} (new)", ResourceGroup.Trim()) : ResourceGroup.Trim();

    public string AdminPlaceholder => L.Get(IsUpdate ? "as before" : "the signed-in account");

    public string AdminText => AdminEmail.Trim() is { Length: > 0 } email ? email : IsUpdate ? L.Get("as before") : Account ?? string.Empty;

    public string ToolsText => Tools.Where(t => t.IsOn != t.Default).Select(t => t.IsOn ? $"+{t.Name}" : $"-{t.Name}").ToList() is { Count: > 0 } changed
        ? string.Join(", ", changed)
        : L.Get("as the app uses them");

    public bool CanGoNext => Step switch
    {
        AzureStep.SignIn => IsSignedIn,
        AzureStep.Subscription => Subscription is not null,
        AzureStep.Target => ResourceGroup.Trim().Length > 0 && GroupError is null && Location is { Length: > 0 } && Name.Trim().Length > 0 && NameError is null,
        _ => false,
    };

    public bool IsDone => Result is not null;

    public bool IsFailed => Run is { IsBusy: false, ExitCode: not null and not 0 };

    public string DeployStatus => Run.IsBusy ? L.Format("Deploying to {0}: building, pushing and deploying takes a few minutes…", ResourceGroup.Trim())
        : Result is not null ? L.Format("Deployed to {0}.", Result.ResourceGroup)
        : Run.ExitCode is { } code and not 0 ? L.Format("coworkee deploy ended with exit code {0}", code)
        : string.Empty;

    public IReadOnlyList<AppEndpoint> Links => Result is null ? [] : [.. Result.Urls.Select(u => new AppEndpoint(u.Key, u.Value))];

    /// <summary>Where the generated administrator password is; the Studio shows the file, never the password.</summary>
    public string PasswordText => L.Format("The administrator's password is in {0} (not in git).", Deployments.SecretsPath(Overview.App.Path, Name.Trim()));

    /// <summary>The arguments after coworkee deploy for the choices made.</summary>
    public IReadOnlyList<string> Arguments() =>
    [
        "azure", "--apply", "--yes", "--resource-group", ResourceGroup.Trim(), "--location", Location!, "--subscription", Subscription!.Id, "--name", Name.Trim(),
        .. OwnSignIn ? ["--own-azure-sign-in"] : _update?.OwnAzureSignIn == true ? ["--own-azure-sign-in", "false"] : Array.Empty<string>(),
        .. AdminEmail.Trim() is { Length: > 0 } email ? ["--admin-email", email] : Array.Empty<string>(),
        .. Tools.Where(t => t.IsOn != t.Default).SelectMany(t => new[] { t.IsOn ? "--with" : "--without", t.Name }),
    ];

    /// <summary>
    /// Is the Azure CLI there and signed in? <c>az account show</c> answers from its cache, only a token proves the sign-in, as in coworkee deploy.
    /// Signed in, the subscriptions are read; an update goes straight to the summary.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanWork))]
    public async Task CheckAsync()
    {
        IsWorking = true;
        SignIn = AzureSignIn.Checking;
        Problem = null;
        try
        {
            SignIn = await SignInStateAsync();
            if (SignIn == AzureSignIn.SignedIn)
            {
                await LoadSubscriptionsAsync();
                if (_update is not null)
                {
                    Subscription = Subscriptions.Where(s => s.Id == _update.SubscriptionId).OrderBy(s => s.User == _update.Account ? 0 : 1).FirstOrDefault();
                    if (Subscription is null)
                    {
                        Problem = L.Format("No signed-in account sees the subscription {0} of this deployment (tenant {1}): sign in with an account that has access.",
                            _update.SubscriptionId, _update.TenantId);
                        SignInTenant = _update.TenantId;
                        Step = AzureStep.Subscription;
                    }
                    else
                    {
                        await LoadGroupsAsync();
                        Step = AzureStep.Summary;
                    }
                }
            }
        }
        catch (Win32Exception)
        {
            SignIn = AzureSignIn.NotInstalled;
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>
    /// az login (to <see cref="SignInTenant"/> when given) opens the browser; the check runs again when it returns. The accounts signed in already stay
    /// signed in; with <see cref="OwnSignIn"/> only the app's own folder is signed in.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanWork))]
    public async Task SignInAsync()
    {
        IsWorking = true;
        try
        {
            if (await AzAsync(["login", .. SignInTenant.Trim() is { Length: > 0 } tenant ? ["--tenant", tenant] : Array.Empty<string>(), "-o", "none"]) is { ExitCode: not 0 } failed)
            {
                Problem = failed.Error;
            }
        }
        catch (Win32Exception)
        {
            SignIn = AzureSignIn.NotInstalled;
            return;
        }
        finally
        {
            IsWorking = false;
        }

        await CheckAsync();
    }

    /// <summary>Turns the Windows broker of the Azure CLI off (its tokens are what get-access-token cannot find), then signs in again.</summary>
    [RelayCommand(CanExecute = nameof(CanWork))]
    public async Task FixBrokerAsync()
    {
        IsWorking = true;
        try
        {
            await AzAsync("config", "set", "core.enable_broker_on_windows=false");
        }
        catch (Win32Exception)
        {
            SignIn = AzureSignIn.NotInstalled;
            return;
        }
        finally
        {
            IsWorking = false;
        }

        await SignInAsync();
    }

    [RelayCommand]
    public async Task NextAsync()
    {
        if (!CanGoNext)
        {
            return;
        }

        Step++;
        if (Step == AzureStep.Target)
        {
            if (AdminEmail.Length == 0 && !IsUpdate)
            {
                AdminEmail = Account ?? string.Empty;
            }

            await LoadGroupsAsync();
        }
    }

    [RelayCommand]
    public void Back()
    {
        if (Run.IsBusy)
        {
            return;
        }

        if (Step == AzureStep.SignIn || IsDone)
        {
            Close();
        }
        else
        {
            Step--;
        }
    }

    [RelayCommand]
    public void PickSubscription(AzureSubscription subscription) => Subscription = subscription;

    [RelayCommand]
    public void PickGroup(AzureResourceGroup group)
    {
        ResourceGroup = group.Name;
        Location = group.Location;
    }

    /// <summary>coworkee deploy azure --apply with the choices; on success the record it wrote shows its addresses and the overview reads it.</summary>
    [RelayCommand]
    public async Task DeployAsync()
    {
        if (Run.IsBusy || Subscription is null)
        {
            return;
        }

        Result = null;
        Step = AzureStep.Deploy;
        var exit = await Run.RunAsync(Arguments(), Environment());
        if (exit == 0)
        {
            Result = Deployments.Read(Deployments.RecordPath(Overview.App.Path, Name.Trim()));
        }

        OnPropertyChanged(nameof(DeployStatus));
        OnPropertyChanged(nameof(IsFailed));
        Overview.Refresh();
    }

    [RelayCommand]
    public void Close()
    {
        Overview.Refresh();
        Main.Page = Overview;
    }

    [RelayCommand]
    public void Open(string url) => Main.Dialogs.Open(url);

    partial void OnStepChanged(AzureStep value)
    {
        if (value == AzureStep.Summary)
        {
            OnPropertyChanged(nameof(GroupText));
            OnPropertyChanged(nameof(AdminText));
            OnPropertyChanged(nameof(ToolsText));
        }
    }

    partial void OnSubscriptionChanged(AzureSubscription? value)
    {
        if (value is { User.Length: > 0 })
        {
            Account = value.User;
        }
    }

    partial void OnResourceGroupChanged(string oldValue, string newValue)
    {
        if (Name.Length == 0 || Name == oldValue.Trim())
        {
            Name = newValue.Trim();
        }
    }

    partial void OnLocationChanged(string? value)
    {
        if (value is { Length: > 0 } && !Regions.Contains(value))
        {
            Regions.Add(value);
        }
    }

    private bool CanWork() => !IsWorking;

    private async Task<AzureSignIn> SignInStateAsync()
    {
        var show = await AzAsync("account", "show", "-o", "json");
        if (show.ExitCode != 0 || show.Output.Length == 0)
        {
            Problem = show.Error;
            return AzureSignIn.SignedOut;
        }

        var account = JsonNode.Parse(show.Output)!;
        Account = account["user"]?["name"]?.GetValue<string>();
        var token = await AzAsync("account", "get-access-token", "--subscription", account["id"]!.GetValue<string>(), "--query", "expiresOn", "-o", "tsv");
        if (token.ExitCode == 0)
        {
            return AzureSignIn.SignedIn;
        }

        Problem = token.Error;
        // "Can't find token from MSAL cache": the sign-in went through the Windows broker
        return $"{token.Error} {token.Output}".Contains("MSAL cache", StringComparison.OrdinalIgnoreCase) ? AzureSignIn.BrokerProblem : AzureSignIn.SignedOut;
    }

    private async Task LoadSubscriptionsAsync()
    {
        // --all: the subscriptions of every signed-in account and tenant; disabled ones cannot take a deployment
        var (exit, output, _) = await AzAsync("account", "list", "--all", "-o", "json");
        var previous = Subscription;
        Subscriptions.Clear();
        foreach (var item in Items(exit == 0 ? output : "[]").Where(i => i["state"]?.GetValue<string>() is null or "Enabled"))
        {
            Subscriptions.Add(new AzureSubscription(item["id"]!.GetValue<string>(), item["name"]?.GetValue<string>() ?? "?", item["tenantId"]?.GetValue<string>() ?? string.Empty,
                item["isDefault"]?.GetValue<bool>() == true, item["user"]?["name"]?.GetValue<string>() ?? string.Empty));
        }

        Subscription = Subscriptions.FirstOrDefault(s => s.Id == previous?.Id && s.User == previous.User) ?? Subscriptions.FirstOrDefault(s => s.IsDefault)
            ?? Subscriptions.FirstOrDefault();
        OnPropertyChanged(nameof(Accounts));
    }

    private async Task LoadGroupsAsync()
    {
        IsWorking = true;
        try
        {
            var (exit, output, _) = await AzAsync("group", "list", "--subscription", Subscription!.Id, "-o", "json");
            Groups.Clear();
            foreach (var item in Items(exit == 0 ? output : "[]"))
            {
                Groups.Add(new AzureResourceGroup(item["name"]!.GetValue<string>(), item["location"]?.GetValue<string>() ?? string.Empty));
            }
        }
        catch (Win32Exception)
        {
            Groups.Clear();
        }
        finally
        {
            IsWorking = false;
            OnPropertyChanged(nameof(IsNewGroup));
        }
    }

    private Task<(int ExitCode, string Output, string Error)> AzAsync(params string[] args) =>
        Main.Generator.CaptureAsync(Deployments.AzureCli, args, CancellationToken.None, Environment());

    // the folder of the app's own sign-in for az and coworkee deploy (and through it the app host and its Azure CLI credential)
    private Dictionary<string, string>? Environment() => OwnSignIn ? new() { [Deployments.AzureConfigDirVariable] = ConfigDir } : null;

    private static IEnumerable<JsonNode> Items(string json)
    {
        try
        {
            return JsonNode.Parse(json) is JsonArray array ? array.OfType<JsonNode>() : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // the rules of Azure for resource group names
    [GeneratedRegex(@"^[-\w.()]{0,89}[-\w()]$")]
    private static partial Regex GroupName();

    // the record is a file name
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex RecordName();
}
