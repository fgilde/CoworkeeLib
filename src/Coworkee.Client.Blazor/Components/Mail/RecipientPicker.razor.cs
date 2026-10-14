using System.ComponentModel.DataAnnotations;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Query;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>Chooses who gets a campaign: a preset or saved list, an own query, or users ticked by hand; always shows who that resolves to.</summary>
public partial class RecipientPicker
{
    private const string QueryChoice = "query";

    private static readonly RecipientPreset[] Presets = Enum.GetValues<RecipientPreset>();
    private static readonly string[] SearchFields = [nameof(UserRow.Email), nameof(UserRow.FirstName), nameof(UserRow.LastName)];

    private readonly QueryGroup _query = new();
    private readonly HashSet<Guid> _roleIds = [];
    private IReadOnlyList<RecipientListDto> _lists = [];
    private IReadOnlyList<RoleDto> _roles = [];
    private IReadOnlyList<QueryField>? _fields;
    private CoworkeeDataTable<UserRow>? _table;
    private RecipientSourceDto? _source;
    private RecipientSourceDto? _kept;
    private RecipientPreviewDto? _preview;
    private string _choice = $"preset:{RecipientPreset.AllActive}";
    private int _tab;
    private int _days = 30;
    private int _version;
    private bool _matching;
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    /// <summary>The recipients of a saved campaign; what the controls cannot show (ticked users, a filter of the table) stays until the selection is changed.</summary>
    [Parameter] public RecipientSourceDto? Initial { get; set; }

    [Parameter] public EventCallback<RecipientSourceDto?> SourceChanged { get; set; }

    [Parameter] public EventCallback<RecipientPreviewDto?> PreviewChanged { get; set; }

    private RecipientPreset? Preset => _choice.StartsWith("preset:", StringComparison.Ordinal) ? Enum.Parse<RecipientPreset>(_choice["preset:".Length..]) : null;

    private RecipientListDto? List => _choice.StartsWith("list:", StringComparison.Ordinal) ? _lists.FirstOrDefault(l => $"list:{l.Id}" == _choice) : null;

    private IReadOnlyList<QueryField> Fields => _fields ??= [.. new[]
        {
            (nameof(UserRow.Email), "Email"),
            (nameof(UserRow.FirstName), "First name"),
            (nameof(UserRow.LastName), "Last name"),
            (nameof(UserRow.IsActive), "Active"),
            (nameof(UserRow.LastLoginAt), "Last sign-in"),
            (nameof(UserRow.CreatedAt), "Created"),
        }.Select(f => DataTableQueryFields.Field(f.Item1, L[f.Item2], typeof(UserRow).GetProperty(f.Item1)!.PropertyType, L)).OfType<QueryField>()];

    protected override async Task OnInitializedAsync()
    {
        await Snackbar.RunAsync(async () =>
        {
            _lists = await Api.GetRecipientListsAsync();
            _roles = await Api.GetRolesAsync();
        });
        if (Initial is not null)
        {
            Restore(Initial);
        }

        await SetSourceAsync(_kept ?? SmartSource());
    }

    private void Restore(RecipientSourceDto source)
    {
        if (source is { Kind: RecipientSourceKind.Preset, Preset: { } preset })
        {
            (_choice, _days) = ($"preset:{preset}", source.Days ?? _days);
            _roleIds.UnionWith(source.RoleIds ?? []);
        }
        else if (source.Kind == RecipientSourceKind.Query && QueryJson.Deserialize(source.QueryJson) is { } query && ODataQueryTranslator.ToFilter(query, Fields) == source.Filter)
        {
            (_query.Logic, _query.Not, _query.Children, _choice) = (query.Logic, query.Not, query.Children, QueryChoice);
        }
        else
        {
            (_kept, _tab, _matching) = (source, 1, source.Kind == RecipientSourceKind.Query);
        }
    }

    private static string PresetLabel(RecipientPreset preset) => preset switch
    {
        RecipientPreset.AllActive => "All active users",
        RecipientPreset.NotSignedInSince => "Not signed in for more than N days",
        RecipientPreset.NeverSignedIn => "Never signed in",
        RecipientPreset.SignedUpWithin => "Signed up in the last N days",
        RecipientPreset.EmailNotConfirmed => "E-mail not confirmed",
        RecipientPreset.InRoles => "Members of roles",
        _ => "Deactivated users",
    };

    private string Translate(string text) => L[text];

    private string RoleName(Guid id) => _roles.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty;

    private RecipientSourceDto? SmartSource() => Preset switch
    {
        RecipientPreset.InRoles => _roleIds.Count == 0 ? null : new(RecipientSourceKind.Preset, Preset, RoleIds: [.. _roleIds]),
        { } preset => new(RecipientSourceKind.Preset, preset, Days: preset is RecipientPreset.NotSignedInSince or RecipientPreset.SignedUpWithin ? _days : null),
        null when List is { } list => list.Source,
        _ => QuerySource(),
    };

    private RecipientSourceDto? QuerySource() =>
        _query.IsEmpty || ODataQueryTranslator.ToFilter(_query, Fields) is not { Length: > 0 } filter
            ? null
            : new(RecipientSourceKind.Query, Filter: filter, QueryJson: QueryJson.Serialize(_query));

    private async Task SetSourceAsync(RecipientSourceDto? source)
    {
        _source = source;
        if (!ReferenceEquals(source, _kept))
        {
            _kept = null;
        }

        await SourceChanged.InvokeAsync(source);
        var version = ++_version;
        RecipientPreviewDto? preview = null;
        if (source is not null)
        {
            await Snackbar.RunAsync(async () => preview = await Api.PreviewRecipientsAsync(source));
        }

        if (version == _version)
        {
            _preview = preview;
            await PreviewChanged.InvokeAsync(preview);
        }
    }

    private Task TabChangedAsync(int tab)
    {
        _tab = tab;
        _matching = false;
        return SetSourceAsync(tab == 0 ? SmartSource() : null);
    }

    private Task ChooseAsync(string choice)
    {
        _choice = choice;
        return SetSourceAsync(SmartSource());
    }

    private Task DaysChangedAsync(int days)
    {
        _days = days;
        return SetSourceAsync(SmartSource());
    }

    private Task RolesChangedAsync(IEnumerable<Guid> roles)
    {
        _roleIds.Clear();
        _roleIds.UnionWith(roles);
        return SetSourceAsync(SmartSource());
    }

    private Task QueryChangedAsync(QueryGroup _) => SetSourceAsync(SmartSource());

    private Task SelectionChangedAsync(HashSet<UserRow> rows)
    {
        _matching = false;
        return SetSourceAsync(rows.Count == 0 ? null : new(RecipientSourceKind.Manual, UserIds: [.. rows.Select(r => r.Id)]));
    }

    // "all matching" follows the filter of the table until the selection is changed
    private Task TableLoadedAsync(IReadOnlyList<UserRow> _) =>
        _matching && _kept is null && _table is { } table && table.CurrentFilter != _source?.Filter ? SelectAllMatchingAsync() : Task.CompletedTask;

    private Task SelectAllMatchingAsync()
    {
        _matching = true;
        var advanced = _table!.Advanced;
        return SetSourceAsync(new(RecipientSourceKind.Query, Filter: _table.CurrentFilter, QueryJson: advanced.IsEmpty ? null : QueryJson.Serialize(advanced)));
    }

    private async Task SaveListAsync()
    {
        var source = _source!;
        RecipientListDto? created = null;
        if (await Dialogs.ShowEditAsync(L["Save as list"], new ListForm(), async form => created = await Api.SaveRecipientListAsync(new SaveRecipientListRequest(form.Name.Trim(), source)),
                meta => meta.Property(f => f.Name).WithLabel(L["Name"])))
        {
            _lists = [.. _lists, created!];
            _choice = $"list:{created!.Id}";
            Snackbar.Add(L["List saved."], Severity.Success);
        }
    }

    private async Task DeleteListAsync(RecipientListDto list)
    {
        if (!await Dialogs.ConfirmAsync(L["Delete"], L["Delete {0}? This cannot be undone.", list.Name], L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteForever))
        {
            return;
        }

        _busy = true;
        try
        {
            if (await Snackbar.RunAsync(() => Api.DeleteRecipientListAsync(list.Id)))
            {
                _lists = [.. _lists.Where(l => l.Id != list.Id)];
                await ChooseAsync($"preset:{RecipientPreset.AllActive}");
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private sealed class ListForm
    {
        [Required]
        [MaxLength(256)]
        public string Name { get; set; } = string.Empty;
    }
}
