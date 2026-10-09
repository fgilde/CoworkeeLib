using System.Reflection;
using Coworkee.Contracts.Configuration;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>
/// Renders properties marked with [ContentTypes], [Cron], [FileSize] or [Translations] with the editors here, in every MudExObjectEdit of the app.
/// AddCoworkeeClient registers it; a meta's RenderWith still wins.
/// </summary>
public sealed class CoworkeeEditors : IDefaultRenderDataProvider
{
    private static readonly CoworkeeEditors Instance = new();

    private static readonly Lazy<bool> Registered = new(() =>
    {
        RenderDataDefaults.AddRenderDataProvider(Instance);
        return true;
    });

    // MudEx keeps the providers in a static list: adding again (or while another form reads it) would break rendering
    public static void Register() => _ = Registered.Value;

    public IRenderData? GetRenderData(ObjectEditPropertyMeta propertyMeta)
    {
        var property = propertyMeta.PropertyInfo;
        var type = property.PropertyType;
        if (property.IsDefined(typeof(CronAttribute)) && type == typeof(string))
        {
            return new RenderData<string, string?>(nameof(CronEditor.Value), typeof(CronEditor));
        }

        if (property.IsDefined(typeof(ContentTypesAttribute)))
        {
            return type == typeof(List<string>) ? ContentTypes<List<string>>(v => [.. v])
                : type == typeof(string[]) ? ContentTypes<string[]>(v => [.. v])
                : null;
        }

        if (property.IsDefined(typeof(FileSizeAttribute)))
        {
            return type == typeof(long?) ? new RenderData<long?, long?>(nameof(FileSizeEditor.Value), typeof(FileSizeEditor))
                : type == typeof(long) ? new RenderData<long, long?>(nameof(FileSizeEditor.Value), typeof(FileSizeEditor)) { ToFieldTypeConverterFn = v => v, ToPropertyTypeConverterFn = v => v ?? 0 }
                : null;
        }

        if (property.IsDefined(typeof(TranslationsAttribute)) && type == typeof(Dictionary<string, string>))
        {
            return new RenderData<Dictionary<string, string>, IReadOnlyDictionary<string, string>>(nameof(TranslationsEditor.Value), typeof(TranslationsEditor))
            {
                ToFieldTypeConverterFn = v => v ?? new Dictionary<string, string>(),
                ToPropertyTypeConverterFn = v => new(v ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            };
        }

        return null;
    }

    private static RenderData<T, IReadOnlyList<string>> ContentTypes<T>(Func<IReadOnlyList<string>, T> toProperty)
        where T : IReadOnlyList<string> =>
        new(nameof(ContentTypesEditor.Value), typeof(ContentTypesEditor))
        {
            ToFieldTypeConverterFn = v => v ?? (IReadOnlyList<string>)[],
            ToPropertyTypeConverterFn = v => toProperty(v ?? []),
        };
}
