using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Cli;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

/// <summary>An existing app at a glance: run it, see where it is deployed, deploy or update it; changing modules and options is one click further.</summary>
public sealed partial class AppOverviewViewModel : ObservableObject
{
    public AppOverviewViewModel(MainViewModel main, MyAppViewModel app)
    {
        Main = main;
        App = app;
        Refresh();
    }

    public MainViewModel Main { get; }

    public MyAppViewModel App { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDeployments))]
    public partial IReadOnlyList<DeploymentItem> Deployments { get; set; } = [];

    public bool HasDeployments => Deployments.Count > 0;

    /// <summary>Reads <c>.coworkee/deployments</c> again and the run status.</summary>
    [RelayCommand]
    public void Refresh()
    {
        Deployments = [.. Coworkee.Cli.Spec.Deployments.ReadAll(App.Path).Select(d => new DeploymentItem(d))];
        App.UpdateStatus();
    }

    /// <summary>The deploy wizard for a new deployment; it checks the Azure CLI sign-in right away.</summary>
    [RelayCommand]
    public Task DeployAsync() => ShowWizard(new AzureDeployViewModel(this));

    /// <summary>The wizard with the values of <paramref name="item"/>: after the sign-in check it shows the summary to deploy the same again.</summary>
    [RelayCommand]
    public Task UpdateAsync(DeploymentItem item) => ShowWizard(new AzureDeployViewModel(this, item.Record));

    /// <summary>The wizard with the state of the app: add modules, entities and options, then apply them with coworkee add.</summary>
    [RelayCommand]
    public void Edit() => Main.OpenApp(App.Path);

    private Task ShowWizard(AzureDeployViewModel wizard)
    {
        Main.Page = wizard;
        return wizard.CheckAsync();
    }
}

/// <summary>One deployment of the app as its record in <c>.coworkee/deployments</c> has it.</summary>
public sealed record DeploymentItem(DeploymentRecord Record)
{
    public string Title => Record.Name;

    public string TargetText => Record.Target == "azure" ? "Azure Container Apps" : Record.Target;

    public string Where => L.Format("Resource group {0} in {1}", Record.ResourceGroup, Record.Location);

    public string Details => L.Format("{0} · last deployed {1}", Record.Account, Record.DeployedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture));

    public IReadOnlyList<AppEndpoint> Links => [.. Record.Urls.Select(u => new AppEndpoint(u.Key, u.Value))];

    public bool CanUpdate => Record.Target == "azure";
}
