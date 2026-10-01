using System.Globalization;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.Configuration;

namespace Coworkee.Settings;

public sealed record SettingDefinition(
    string Name,
    string Group,
    string DisplayName,
    SettingType Type,
    IReadOnlyList<SettingScope> Scopes,
    string? DefaultValue,
    bool VisibleToClient,
    string? Description,
    IReadOnlyList<string>? Choices)
{
    public bool IsEncrypted => Type == SettingType.Secret;

    public bool IsClientVisible => VisibleToClient && !IsEncrypted;

    public string? Validate(string value) => Type switch
    {
        SettingType.Bool when !bool.TryParse(value, out _) => "Expected true or false.",
        SettingType.Int when !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) => "Expected a whole number.",
        SettingType.Choice when Choices?.Contains(value, StringComparer.Ordinal) != true => $"Expected one of: {string.Join(", ", Choices ?? [])}.",
        _ => null,
    };
}

public interface ISettingDefinitionContributor
{
    void Define(SettingDefinitionContext context);
}

public sealed class SettingDefinitionContext
{
    internal List<(string Name, string DisplayName)> Groups { get; } = [];

    internal List<SettingDefinition> Definitions { get; } = [];

    public SettingGroupBuilder Group(string name, string displayName)
    {
        if (!Groups.Exists(g => g.Name == name))
        {
            Groups.Add((name, displayName));
        }

        return new SettingGroupBuilder(this, name);
    }
}

public sealed class SettingGroupBuilder
{
    private readonly SettingDefinitionContext _context;
    private readonly string _group;

    internal SettingGroupBuilder(SettingDefinitionContext context, string group)
    {
        _context = context;
        _group = group;
    }

    public SettingGroupBuilder Add(
        string name,
        string displayName,
        SettingType type,
        SettingScope[] scopes,
        string? defaultValue = null,
        bool visibleToClient = false,
        string? description = null,
        string[]? choices = null)
    {
        _context.Definitions.Add(new SettingDefinition(name, _group, displayName, type, scopes, defaultValue, visibleToClient, description, choices));
        return this;
    }
}

public interface ISettingDefinitionManager
{
    IReadOnlyList<SettingDefinition> All { get; }

    IReadOnlyList<(string Name, string DisplayName)> Groups { get; }

    SettingDefinition? Find(string name);
}

internal sealed class SettingDefinitionManager : ISettingDefinitionManager
{
    private readonly Dictionary<string, SettingDefinition> _byName;

    public SettingDefinitionManager(IEnumerable<ISettingDefinitionContributor> contributors, IConfiguration configuration)
    {
        var context = new SettingDefinitionContext();
        foreach (var contributor in contributors)
        {
            contributor.Define(context);
        }

        var defaults = configuration.GetSection("Coworkee:Settings:Defaults");
        All = context.Definitions
            .Select(d => defaults[d.Name] is { } configured ? d with { DefaultValue = configured } : d)
            .ToList();
        Groups = context.Groups;
        _byName = All.ToDictionary(d => d.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<SettingDefinition> All { get; }

    public IReadOnlyList<(string Name, string DisplayName)> Groups { get; }

    public SettingDefinition? Find(string name) => _byName.GetValueOrDefault(name);
}
