using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.ExtendedAttributes;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.ExtendedAttributes;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Lists and edits the extended attributes of one entity registered with AddExtendedAttributes on the server.</summary>
public partial class ExtendedAttributesEditor
{
    private List<ExtendedAttributeDto> _attributes = [];
    private bool _saving;

    [Inject] private IExtendedAttributesApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid EntityId { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<ExtendedAttributeDto>> Saved { get; set; }

    protected override Task OnParametersSetAsync() =>
        Snackbar.RunAsync(async () => _attributes = [.. await Api.GetAsync(EntityType, EntityId)]);

    private void Add() => _attributes.Add(new ExtendedAttributeDto());

    private static void SetDate(ExtendedAttributeDto attribute, DateTime? date) =>
        attribute.DateTime = date is { } value ? new DateTimeOffset(value) : null;

    private async Task SaveAsync()
    {
        _saving = true;
        IReadOnlyList<ExtendedAttributeDto> saved = [];
        if (await Snackbar.RunAsync(async () => saved = await Api.SaveAsync(EntityType, EntityId, _attributes), L["Saved"]))
        {
            _attributes = [.. saved];
            await Saved.InvokeAsync(saved);
        }

        _saving = false;
    }
}
