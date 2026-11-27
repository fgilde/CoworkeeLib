using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>The values of a chips chart that get a chip, in order, each with an icon.</summary>
public partial class ValueIconEditor
{
    /// <summary>The icons to choose from: id, tooltip (translated) and icon.</summary>
    public static readonly IReadOnlyList<(string Name, string Label, string Svg)> Choices =
    [
        ("Flight", "Plane", Icons.Material.Outlined.Flight),
        ("DirectionsBoat", "Ship", Icons.Material.Outlined.DirectionsBoat),
        ("LocalShipping", "Truck", Icons.Material.Outlined.LocalShipping),
        ("Train", "Train", Icons.Material.Outlined.Train),
        ("DirectionsCar", "Car", Icons.Material.Outlined.DirectionsCar),
        ("Inventory2", "Parcel", Icons.Material.Outlined.Inventory2),
        ("Warehouse", "Warehouse", Icons.Material.Outlined.Warehouse),
        ("Public", "Globe", Icons.Material.Outlined.Public),
        ("Schedule", "Clock", Icons.Material.Outlined.Schedule),
        ("CheckCircle", "Done", Icons.Material.Outlined.CheckCircle),
        ("Warning", "Warning", Icons.Material.Outlined.Warning),
        ("Cancel", "Cancel", Icons.Material.Outlined.Cancel),
        ("Star", "Star", Icons.Material.Outlined.Star),
        ("Flag", "Flag", Icons.Material.Outlined.Flag),
        ("Person", "Person", Icons.Material.Outlined.Person),
        ("Label", "Label", Icons.Material.Outlined.Label),
    ];

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public ReportWidget Widget { get; set; } = null!;

    [Parameter, EditorRequired] public ReportState State { get; set; } = null!;

    [Parameter] public EventCallback OnChanged { get; set; }

    private WidgetOptions Options => Widget.Options;

    private Task Change(Action change)
    {
        change();
        return OnChanged.InvokeAsync();
    }

    // a value that another chip has already is refused (the field shows the old one again)
    private Task Rename(int index, string? value) => Change(() =>
    {
        var name = value?.Trim() ?? string.Empty;
        if (!Options.ValueIcons.Where((_, i) => i != index).Any(v => v.Value == name))
        {
            Options.ValueIcons[index] = Options.ValueIcons[index] with { Value = name };
        }
    });

    private Task Move(int from, int to) => Change(() =>
    {
        var chip = Options.ValueIcons[from];
        Options.ValueIcons.RemoveAt(from);
        Options.ValueIcons.Insert(to, chip);
    });

    // a new chip starts with the next member (its name, which is what the data's keys carry) of the group-by field that has none yet
    private Task Add() => Change(() =>
    {
        var members = State.Field(Widget.Query?.EntitySet, Widget.Query?.GroupBy?.Field)?.Members ?? [];
        var next = members.FirstOrDefault(m => Options.ValueIcons.All(v => v.Value != m)) ?? string.Empty;
        Options.ValueIcons.Add(new ValueIcon(next, Choices[^1].Svg));
    });
}
