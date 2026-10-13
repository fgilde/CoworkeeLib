using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Settings;
using MudBlazor;
using MudBlazor.Extensions.Components;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;
using Nextended.Core.Helper;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// The settings of a scope as one object MudExObjectEdit can edit: a type built at runtime with a property per setting
/// (bool, int? or string by <see cref="SettingType"/>), so every setting a module defines shows up without own form code.
/// </summary>
public sealed partial class SettingsModel
{
    private static readonly ConcurrentDictionary<string, Type> Types = new(StringComparer.Ordinal);

    private readonly IReadOnlyList<(SettingGroupDto Group, SettingDefinitionDto Setting)> _settings;
    private readonly IReadOnlyDictionary<string, SettingValueDto> _stored;
    private readonly Dictionary<string, string?> _initial = new(StringComparer.Ordinal);

    public SettingsModel(IReadOnlyList<SettingGroupDto> groups, IReadOnlyDictionary<string, SettingValueDto> stored)
    {
        _settings = [.. groups.SelectMany(g => g.Settings.Select(s => (g, s)))];
        _stored = stored;
        var type = Types.GetOrAdd(string.Join('|', _settings.Select(s => $"{s.Setting.Name}:{s.Setting.Type}")), _ => BuildType(_settings.Select(s => s.Setting)));
        Value = Activator.CreateInstance(type)!;
        foreach (var (_, setting) in _settings)
        {
            var initial = Initial(setting);
            Property(setting).SetValue(Value, Parse(setting, initial));
            _initial[setting.Name] = Format(Property(setting).GetValue(Value));
        }
    }

    /// <summary>The object the form edits.</summary>
    public object Value { get; }

    /// <summary>Label, help text, group and editor of every setting; secrets as password, choices and languages as select.</summary>
#pragma warning disable BL0005 // MudEx configures the rendered editors through these instances
    public void Configure(ObjectEditMeta<object> meta, CoworkeeLocalizer l, SettingScope scope)
    {
        SettingsFormRules.Apply(meta);
        foreach (var (group, setting) in _settings)
        {
            var property = meta.Property(PropertyName(setting.Name))!
                .WithLabel(l[Label(setting, scope)])
                .WithGroup(l[group.DisplayName]);
            if (Description(setting, l) is { Length: > 0 } description)
            {
                property.WithDescription(description);
            }

            switch (setting.Type)
            {
                case SettingType.Secret:
                    property.WithAdditionalAttribute(nameof(MudTextField<string>.InputType), InputType.Password).IgnoreOnExportAndImport();
                    break;
                case SettingType.Text:
                    property.WithAdditionalAttribute(nameof(MudTextField<string>.Lines), 4);
                    break;
                case SettingType.Choice:
                    property.RenderWith<MudExSelect<string>, string?>(s => s.Value, s =>
                    {
                        s.ItemCollection = [.. setting.Choices ?? []];
                        s.ToStringFunc = choice => choice is null ? string.Empty : l.Find(choice) ?? choice;
                    });
                    break;
                case SettingType.Language:
                {
                    var empty = l[scope == SettingScope.Global ? "Configured default" : "Inherited"];
                    property.RenderWith<MudExSelect<string>, string?>(s => s.Value, s =>
                    {
                        s.ItemCollection = [string.Empty, .. l.Languages.Select(c => c.Culture)];
                        s.ToStringFunc = culture => string.IsNullOrEmpty(culture) ? empty : l.Languages.FirstOrDefault(c => c.Culture == culture)?.Name ?? culture!;
                    });
                    break;
                }
            }

            if (setting.DefaultValue is { Length: > 0 } fallback && setting.Type is SettingType.String or SettingType.Int)
            {
                property.WithAdditionalAttribute(nameof(MudTextField<string>.Placeholder), fallback);
            }

            if (setting.EnabledWhen is { } condition && Value.GetType().GetProperty(PropertyName(condition.Setting)) is { } dependency)
            {
                bool Disabled(object m) => !condition.Values.Contains(Format(dependency.GetValue(m)));
                // MudEx's AsDisabledIf only drops the attribute again, which leaves the field disabled: Blazor keeps a parameter that is no longer passed
                property.WithAttributesIf<object>(Disabled, new KeyValuePair<string, object>(nameof(MudBaseInput<string>.Disabled), true))
                    .WithAttributesIf<object>(m => !Disabled(m), new KeyValuePair<string, object>(nameof(MudBaseInput<string>.Disabled), false));
            }
        }

        SettingsFormRules.WrapInColumns(meta);
    }
#pragma warning restore BL0005

    private static string Label(SettingDefinitionDto setting, SettingScope scope) =>
        setting.Type == SettingType.Language && scope == SettingScope.User ? "Language" : setting.DisplayName;

    private string? Description(SettingDefinitionDto setting, CoworkeeLocalizer l)
    {
        var description = setting.Description is { Length: > 0 } text ? l[text] : null;
        if (setting.Type != SettingType.Secret)
        {
            return description;
        }

        var stored = l[_stored.GetValueOrDefault(setting.Name)?.HasValue == true ? "A value is stored. Type to replace it." : "No value stored."];
        return description is null ? stored : $"{description} {stored}";
    }

    /// <summary>Takes the values of an exported file; secrets are never imported, and settings the file lacks stay as they are.</summary>
    public void Import(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var (_, setting) in _settings.Where(s => s.Setting.Type != SettingType.Secret))
        {
            if (document.RootElement.TryGetProperty(PropertyName(setting.Name), out var element))
            {
                var text = element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
                Property(setting).SetValue(Value, Parse(setting, text));
            }
        }
    }

    /// <summary>What changed since loading, as the API stores it: invariant text, null to fall back to the default; secrets only when typed.</summary>
    public IReadOnlyDictionary<string, string?> Changes()
    {
        var changes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (_, setting) in _settings)
        {
            var value = Format(Property(setting).GetValue(Value));
            if (setting.Type == SettingType.Secret ? value is not null : value != _initial[setting.Name])
            {
                changes[setting.Name] = value;
            }
        }

        return changes;
    }

    /// <summary>"Mail.Smtp.Host" becomes "Mail_Smtp_Host": MudEx reads dots as a path.</summary>
    public static string PropertyName(string setting) => NotIdentifier().Replace(setting, "_");

    private string? Initial(SettingDefinitionDto setting)
    {
        var stored = _stored.GetValueOrDefault(setting.Name)?.Value;
        return setting.Type switch
        {
            SettingType.Secret => null,
            SettingType.Bool or SettingType.Choice => stored ?? setting.DefaultValue, // a box or select cannot show "not set"
            SettingType.Language => stored ?? string.Empty, // the select lists the empty option as "", not null
            _ => stored,
        };
    }

    private PropertyInfo Property(SettingDefinitionDto setting) => Value.GetType().GetProperty(PropertyName(setting.Name))!;

    private static Type BuildType(IEnumerable<SettingDefinitionDto> settings)
    {
        var builder = new TypeExtender($"Settings_{Guid.NewGuid():N}");
        foreach (var setting in settings)
        {
            builder.AddProperty(PropertyName(setting.Name), ClrType(setting.Type));
        }

        return builder.FetchType();
    }

    private static Type ClrType(SettingType type) => type switch
    {
        SettingType.Bool => typeof(bool),
        SettingType.Int => typeof(int?),
        _ => typeof(string),
    };

    private static object? Parse(SettingDefinitionDto setting, string? text) => setting.Type switch
    {
        SettingType.Bool => bool.TryParse(text, out var on) && on,
        SettingType.Int => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null,
        _ => text,
    };

    private static string? Format(object? value) => value switch
    {
        bool on => on ? "true" : "false",
        int number => number.ToString(CultureInfo.InvariantCulture),
        string { Length: > 0 } text => text,
        _ => null,
    };

    [GeneratedRegex("[^A-Za-z0-9_]")]
    private static partial Regex NotIdentifier();
}
