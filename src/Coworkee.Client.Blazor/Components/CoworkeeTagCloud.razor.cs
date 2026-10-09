using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Social;
using Coworkee.Contracts.Social;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// The tag set of an entity type with counts; choosing a tag yields an OData filter on the ids of its entities,
/// e.g. <c>&lt;CoworkeeTagCloud EntityType="Products" @bind-Filter="_tagFilter" /&gt;</c> next to a CoworkeeDataTable with Filter="_tagFilter".
/// </summary>
public partial class CoworkeeTagCloud
{
    private IReadOnlyList<TagDto> _tags = [];
    private string? _selected;

    [Inject] private ISocialApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;

    [Parameter] public string? Filter { get; set; }

    [Parameter] public EventCallback<string?> FilterChanged { get; set; }

    [Parameter] public EventCallback<string?> TagChanged { get; set; }

    protected override Task OnParametersSetAsync() => Snackbar.RunAsync(async () => _tags = await Api.GetTagsAsync(EntityType));

    private async Task SelectAsync(string? tag)
    {
        _selected = tag;
        string? filter = null;
        if (tag is null || await Snackbar.RunAsync(async () => filter = ToFilter(await Api.GetTaggedAsync(EntityType, tag))))
        {
            await TagChanged.InvokeAsync(tag);
            await FilterChanged.InvokeAsync(filter);
        }
    }

    // ponytail: the ids travel in the URL; a tag on thousands of entities needs a server side OData function instead
    internal static string ToFilter(IReadOnlyList<Guid> ids) => ids.Count == 0 ? "false" : $"Id in ({string.Join(",", ids)})";
}
