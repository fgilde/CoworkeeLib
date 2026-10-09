using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Social;
using Coworkee.Contracts.Social;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Tags of one entity registered with AddCoworkeeSocial(s => s.Tags(...)); suggests the tags of the entity type and takes new ones.</summary>
public partial class CoworkeeTags
{
    private List<string> _tags = [];
    private bool _canEdit;
    private string? _input;

    [Inject] private ISocialApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid EntityId { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<string>> Changed { get; set; }

    protected override Task OnParametersSetAsync() => Snackbar.RunAsync(async () => Apply(await Api.GetEntityTagsAsync(EntityType, EntityId)));

    private async Task<IEnumerable<string>> SearchAsync(string? text, CancellationToken cancellationToken)
    {
        var known = (await Api.GetTagsAsync(EntityType, cancellationToken)).Select(t => t.Name).Except(_tags, StringComparer.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(text) ? known : known.Where(t => t.Contains(text.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    private async Task KeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await AddAsync();
        }
    }

    private Task AddAsync() =>
        string.IsNullOrWhiteSpace(_input) || _tags.Contains(_input.Trim(), StringComparer.OrdinalIgnoreCase)
            ? Task.CompletedTask
            : SaveAsync([.. _tags, _input.Trim()]);

    private Task RemoveAsync(string tag) => SaveAsync([.. _tags.Where(t => t != tag)]);

    private async Task SaveAsync(IReadOnlyList<string> tags)
    {
        if (await Snackbar.RunAsync(async () => Apply(await Api.SaveEntityTagsAsync(EntityType, EntityId, tags))))
        {
            _input = null;
            await Changed.InvokeAsync(_tags);
        }
    }

    private void Apply(EntityTagsDto tags)
    {
        _tags = [.. tags.Tags];
        _canEdit = tags.CanEdit && !ReadOnly;
    }
}
