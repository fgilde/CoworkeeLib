using System.Globalization;
using Coworkee.Application.Retention;
using Coworkee.Contracts.Settings;
using Coworkee.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Retention;

/// <summary>One Global setting <c>Retention.{key}.Days</c> per target; its group has an own page, so the settings tabs leave it out.</summary>
internal sealed class RetentionSettingDefinitions(IServiceScopeFactory scopes) : ISettingDefinitionContributor
{
    public const string Group = "Retention";

    public const int MaxDays = 36500;

    public static string Name(string key) => $"Retention.{key}.Days";

    /// <summary>Empty, 0 or less: never delete.</summary>
    public static int? Days(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days > 0 ? days : null;

    public void Define(SettingDefinitionContext context)
    {
        using var scope = scopes.CreateScope();
        var group = context.Group(Group, "Retention", page: "/admin/retention");
        foreach (var target in scope.ServiceProvider.GetServices<IRetentionTarget>())
        {
            group.Add(Name(target.Key), target.Title, SettingType.Int, [SettingScope.Global], target.DefaultDays?.ToString(CultureInfo.InvariantCulture),
                description: "Days to keep; empty or 0 keeps everything.");
        }
    }
}
