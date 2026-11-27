using System.Globalization;
using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Settings;
using Coworkee.Client.Blazor.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions.Components;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.ClientEntities.Pages;

/// <summary>
/// Every entity set the browsers could keep a copy of, with its settings and what decided them (code or an override); an administrator turns copies on
/// or off, picks memory or SQLite and tunes the sync. The overrides are the app configuration section <see cref="ClientEntitySetOverride.Section"/>.
/// </summary>
public partial class LocalCopies : IAsyncDisposable
{
    private IReadOnlyList<ClientEntityAdminSetDto> _sets = [];
    private bool _busy;
    private bool _canCopy;
    private bool _canFilter;
    private HashSet<string> _noCopy = [];
    private bool _sqlite;
    private IAsyncDisposable? _subscription;
    private bool _realtime;
    private IReadOnlyList<ClientEntityAdminSetDto> _visible = [];
    private string? _search;
    private bool _onlyCopies;
    private bool _onlyFiltered;
    private DataFilterOverviewDto? _overview;
    private Dictionary<string, DataFilterSetStateDto> _states = new(StringComparer.OrdinalIgnoreCase);

    [Inject] private ClientEntityApi Sets { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IServiceProvider Services { get; set; } = null!;

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    protected override async Task OnInitializedAsync()
    {
        _canCopy = await Permissions.HasAsync(SettingsPermissions.Manage);
        _canFilter = await Permissions.HasAsync([DataFilterPermissions.ManageSystem, DataFilterPermissions.ManageTenant], PermissionMatch.Any);
        _sqlite = Services.GetService<IClientEntityEngine>() is { IsAvailable: true };
        await LoadAsync();
        // a change of another administrator shows at once
        if (Services.GetService<RealtimeClient>() is { } realtime)
        {
            _realtime = true;
            _subscription = await realtime.SubscribeAsync(ClientEntityTopics.Changed, _ => InvokeAsync(async () =>
            {
                await Task.Delay(ClientEntities.SettingsDelay);
                await LoadAsync();
                StateHasChanged();
            }));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is { } subscription)
        {
            await subscription.DisposeAsync();
        }
    }

    private async Task LoadAsync()
    {
        _busy = true;
        try
        {
            await Snackbar.RunAsync(async () =>
            {
                // every set a filter can be written for; the copy settings exist only for those that can have a local copy
                IReadOnlyList<ClientEntityAdminSetDto> copies = _canCopy ? await Sets.AdminAsync(CancellationToken.None) : [];
                var names = _canFilter ? await Api.GetDataFilterSetsAsync() : [];
                _noCopy = [.. names.Except(copies.Select(s => s.EntitySet), StringComparer.OrdinalIgnoreCase)];
                await LoadOverviewAsync();
                _sets = [.. copies.Concat(_noCopy.Select(NoCopy)).OrderBy(s => s.EntitySet, StringComparer.OrdinalIgnoreCase)];
            });
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private async Task LoadOverviewAsync()
    {
        if (_canFilter)
        {
            _overview = await Api.GetDataFilterOverviewAsync();
            _states = new(_overview?.Sets.ToDictionary(s => s.EntitySet, StringComparer.OrdinalIgnoreCase) ?? [], StringComparer.OrdinalIgnoreCase);
        }
    }

    private Task ReloadOverviewAsync() => InvokeAsync(async () =>
    {
        await Snackbar.RunAsync(LoadOverviewAsync);
        Refresh();
        StateHasChanged();
    });

    private static ClientEntityAdminSetDto NoCopy(string entitySet) =>
        new(entitySet, string.Empty, null, 0, 0, 0, TimeSpan.Zero, ClientEntityDefaultMode.Client, new Dictionary<string, ClientEntitySettingSource>(), null);

    private async Task EditAsync(ClientEntityAdminSetDto set)
    {
        if (await Dialogs.ShowEditAsync(L["Local copy of {0}", set.EntitySet], LocalCopyForm.From(set.Override), f => SaveAsync(set.EntitySet, f.ToOverride()), meta => Meta(meta, set)))
        {
            await LoadAsync();
        }
    }

    private async Task ResetAsync(ClientEntityAdminSetDto set)
    {
        _busy = true;
        try
        {
            await Snackbar.RunAsync(() => SaveAsync(set.EntitySet, null), L["{0} follows the code again.", set.EntitySet]);
        }
        finally
        {
            _busy = false;
        }

        await LoadAsync();
    }

    // the whole section goes back with this set changed; the server keeps only what differs from appsettings
    private async Task SaveAsync(string entitySet, ClientEntitySetOverride? change)
    {
        var current = await Api.GetAppConfigurationAsync(ClientEntitySetOverride.Section);
        var all = new Dictionary<string, ClientEntitySetOverride>(
            current.Values.ValueKind == JsonValueKind.Object ? current.Values.Deserialize<Dictionary<string, ClientEntitySetOverride>>(JsonSerializerOptions.Web) ?? [] : [],
            StringComparer.OrdinalIgnoreCase);
        all.Remove(entitySet);
        if (change is { IsEmpty: false })
        {
            all[entitySet] = change;
        }

        await Api.SaveAppConfigurationAsync(ClientEntitySetOverride.Section, JsonSerializer.SerializeToElement(all, JsonSerializerOptions.Web));
    }

#pragma warning disable BL0005 // MudEx configures the rendered editors through these instances
    private void Meta(ObjectEditMeta<LocalCopyForm> meta, ClientEntityAdminSetDto set)
    {
        string code = L["As the code says"];
        meta.Property(f => f.Copy).WithLabel(L["Local copy"]).RenderWith<MudExSelect<string>, string?>(s => s.Value, s =>
        {
            s.ItemCollection = ["", nameof(ClientEntityCopy.Off), nameof(ClientEntityCopy.Memory), nameof(ClientEntityCopy.Sqlite)];
            s.ToStringFunc = choice => string.IsNullOrEmpty(choice) ? code : CopyText(Enum.Parse<ClientEntityCopy>(choice));
            s.HelperText = _sqlite ? L["Memory: columns in memory, kept in IndexedDB. SQLite: a database in the browser."] : L["SQLite not available in this app"];
        });
        meta.Property(f => f.MaxRows).WithLabel(L["Row limit"]).WithAdditionalAttribute("HelperText", Now(set.MaxRows.ToString("N0", Culture)));
        meta.Property(f => f.BatchSize).WithLabel(L["Batch"]).WithAdditionalAttribute("HelperText", Now(set.BatchSize.ToString("N0", Culture)));
        meta.Property(f => f.ParallelPages).WithLabel(L["Parallel pages"]).WithAdditionalAttribute("HelperText", Now(set.ParallelPages.ToString(Culture)));
        meta.Property(f => f.RefreshSeconds).WithLabel(L["Refresh (seconds)"]).WithAdditionalAttribute("HelperText", Now(Interval(set.RefreshInterval)));
        meta.Property(f => f.DefaultMode).WithLabel(L["Default mode"]).RenderWith<MudExSelect<string>, string?>(s => s.Value, s =>
        {
            s.ItemCollection = ["", nameof(ClientEntityDefaultMode.Client), nameof(ClientEntityDefaultMode.Server)];
            s.ToStringFunc = choice => string.IsNullOrEmpty(choice) ? code : ModeText(Enum.Parse<ClientEntityDefaultMode>(choice));
            s.HelperText = L["Where queries run for users who did not choose themselves."];
        });
    }
#pragma warning restore BL0005

    private string Now(string value) => L["Empty: as the code says, now {0}", value];

    private string CopyText(ClientEntityCopy? copy) => copy switch
    {
        ClientEntityCopy.Off => L["Server only"],
        ClientEntityCopy.Memory => L["Memory"],
        ClientEntityCopy.Sqlite => "SQLite",
        _ => L["Automatic"],
    };

    private static Color CopyColor(ClientEntityCopy? copy) => copy is ClientEntityCopy.Off ? Color.Default : Color.Primary;

    private static string CopyIcon(ClientEntityCopy? copy) => copy switch
    {
        ClientEntityCopy.Off => Icons.Material.Outlined.Cloud,
        ClientEntityCopy.Sqlite => Icons.Material.Outlined.Storage,
        _ => Icons.Material.Outlined.Memory,
    };

    private string ModeText(ClientEntityDefaultMode mode) => mode == ClientEntityDefaultMode.Server ? L["Server"] : L["Browser"];

    private string SourceText(ClientEntitySettingSource source) => source switch
    {
        ClientEntitySettingSource.Attribute => L["Attribute"],
        ClientEntitySettingSource.Code => L["Code"],
        ClientEntitySettingSource.Service => L["Service"],
        ClientEntitySettingSource.Override => L["Override"],
        _ => L["Default"],
    };

    private string Interval(TimeSpan interval) =>
        interval <= TimeSpan.Zero ? L["Only on changes"]
        : interval.TotalSeconds % 60 != 0 ? L["{0} s", interval.TotalSeconds.ToString(Culture)]
        : interval.TotalMinutes % 60 != 0 ? L["{0} min", interval.TotalMinutes.ToString(Culture)]
        : L["{0} h", interval.TotalHours.ToString(Culture)];

    /// <summary>The fields of the dialog; empty ones keep what the code says.</summary>
    public sealed class LocalCopyForm
    {
        public string Copy { get; set; } = string.Empty;

        public int? MaxRows { get; set; }

        public int? BatchSize { get; set; }

        public int? ParallelPages { get; set; }

        public int? RefreshSeconds { get; set; }

        public string DefaultMode { get; set; } = string.Empty;

        public static LocalCopyForm From(ClientEntitySetOverride? set) => new()
        {
            Copy = set?.Copy?.ToString() ?? string.Empty,
            MaxRows = set?.MaxRows,
            BatchSize = set?.BatchSize,
            ParallelPages = set?.ParallelPages,
            RefreshSeconds = set?.RefreshInterval is { } interval ? (int)interval.TotalSeconds : null,
            DefaultMode = set?.DefaultMode?.ToString() ?? string.Empty,
        };

        public ClientEntitySetOverride ToOverride() => new()
        {
            Copy = Enum.TryParse<ClientEntityCopy>(Copy, out var copy) ? copy : null,
            MaxRows = MaxRows is > 0 ? MaxRows : null,
            BatchSize = BatchSize is > 0 ? BatchSize : null,
            ParallelPages = ParallelPages is > 0 ? ParallelPages : null,
            RefreshInterval = RefreshSeconds is int seconds and >= 0 ? TimeSpan.FromSeconds(seconds) : null,
            DefaultMode = Enum.TryParse<ClientEntityDefaultMode>(DefaultMode, out var mode) ? mode : null,
        };
    }
}
