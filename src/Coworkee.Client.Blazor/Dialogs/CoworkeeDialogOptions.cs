using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Settings;
using MudBlazor;
using MudBlazor.Extensions.Core;
using MudBlazor.Extensions.Options;

namespace Coworkee.Client.Blazor.Dialogs;

/// <summary>
/// The options the library opens its dialogs with: the app's <see cref="DialogOptionsContributor"/>, and the administrator's
/// <see cref="DialogSettings.Options"/> over it when the contributor allows it (<see cref="DialogOptionsContributor.AdminEditable"/>).
/// </summary>
public sealed class CoworkeeDialogOptions(DialogOptionsContributor contributor, ThemeService themes)
{
    private static readonly DialogOptionsContributor Library = new();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new TimingFunctionConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OnlyWhatMatters } },
    };

    // ponytail: process-wide like DialogPlacement, fine for WebAssembly where one user owns the process; CoworkeeLayout sets it.
    // The static helpers (EditDialogExtensions.Small() and the like) have no services to ask.
    /// <summary>The provider of the running app; without it the library's own defaults apply.</summary>
    public static CoworkeeDialogOptions? Current { get; set; }

    /// <summary>The options of <paramref name="part"/> from <see cref="Current"/>, or the library's defaults.</summary>
    public static DialogOptionsEx For(DialogPart part, bool clickedLeft = false) => Current?.Get(part, clickedLeft) ?? Library.Of(part, clickedLeft);

    public DialogOptionsContributor Contributor => contributor;

    /// <summary>A new instance each call: the administrator's options when allowed and set, otherwise the app's; a side sheet keeps the clicked side.</summary>
    public DialogOptionsEx Get(DialogPart part, bool clickedLeft = false)
    {
        if (!contributor.AdminEditable || !Parse(themes.ClientSettings.GetValueOrDefault(DialogSettings.Options)).TryGetValue(part, out var options))
        {
            return contributor.Of(part, clickedLeft);
        }

        if (part == DialogPart.SideSheet)
        {
            options.Position = clickedLeft ? DialogPosition.CenterLeft : DialogPosition.CenterRight;
        }

        return options;
    }

    /// <summary>The parts of the setting; unknown parts and a broken setting are left out.</summary>
    public static IReadOnlyDictionary<DialogPart, DialogOptionsEx> Parse(string? json)
    {
        var parts = new Dictionary<DialogPart, DialogOptionsEx>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return parts;
        }

        try
        {
            foreach (var (name, value) in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, Json) ?? [])
            {
                if (Enum.TryParse<DialogPart>(name, ignoreCase: true, out var part) && Enum.IsDefined(part) && value.Deserialize<DialogOptionsEx>(Json) is { } options)
                {
                    parts[part] = options;
                }
            }
        }
        catch (JsonException)
        {
            return new Dictionary<DialogPart, DialogOptionsEx>();
        }

        return parts;
    }

    /// <summary>The setting for <paramref name="parts"/>; null when there are none.</summary>
    public static string? Serialize(IReadOnlyDictionary<DialogPart, DialogOptionsEx> parts) =>
        parts.Count == 0 ? null : JsonSerializer.Serialize(parts.ToDictionary(p => p.Key.ToString(), p => p.Value), Json);

    /// <summary>Whether two options are the same, as they would be saved.</summary>
    public static bool Same(DialogOptionsEx a, DialogOptionsEx b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);

    // computed values are not written, and neither are the empty appearances the object editor puts in: the options stay comparable
    private static void OnlyWhatMatters(JsonTypeInfo info)
    {
        foreach (var property in info.Properties)
        {
            if (property.Set is null)
            {
                property.ShouldSerialize = (_, _) => false;
            }
            else if (property.PropertyType == typeof(MudExAppearance))
            {
                property.ShouldSerialize = (_, value) => value is MudExAppearance appearance && (!string.IsNullOrEmpty(appearance.Class) || !string.IsNullOrEmpty(appearance.Style));
            }
        }
    }

    // MudEx writes it as an object it cannot read back; it is a CSS value such as "ease-in"
    private sealed class TimingFunctionConverter : JsonConverter<AnimationTimingFunction>
    {
        public override AnimationTimingFunction? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString() is { Length: > 0 } value ? value : null;

        public override void Write(Utf8JsonWriter writer, AnimationTimingFunction value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
