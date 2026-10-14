using System.Collections;
using System.Globalization;
using Coworkee.Contracts.Mailing;
using Scriban.Runtime;

namespace Coworkee.Mailing;

public static class MailTemplateVariables
{
    private const int MaxDepth = 4;

    public static IReadOnlyList<MailVariableDto> From(object sample)
    {
        var result = new List<MailVariableDto>();
        Walk(sample, null, result, 1);
        return result;
    }

    private static void Walk(object value, string? prefix, List<MailVariableDto> result, int depth)
    {
        foreach (var (name, member, declared) in Members(value))
        {
            var path = prefix is null ? name : $"{prefix}.{name}";
            var type = member?.GetType() ?? declared;
            if (type != typeof(string) && type.IsAssignableTo(typeof(IEnumerable)))
            {
                result.Add(new MailVariableDto(path, "list", null, true));
                if (depth < MaxDepth && member is IEnumerable items && items.Cast<object?>().FirstOrDefault() is { } first && Kind(first.GetType()) == "object")
                {
                    Walk(first, path + "[]", result, depth + 1);
                }
            }
            else if (member is not null && Kind(type) == "object" && depth < MaxDepth)
            {
                Walk(member, path, result, depth + 1);
            }
            else
            {
                result.Add(new MailVariableDto(path, Kind(type), member is null ? null : Convert.ToString(member, CultureInfo.InvariantCulture), false));
            }
        }
    }

    private static IEnumerable<(string Name, object? Value, Type Declared)> Members(object value) =>
        value is ScriptObject script
            ? script.Keys.Select(key => (key, script[key], typeof(object)))
            : value.GetType().GetProperties().Select(p => (StandardMemberRenamer.Default(p), p.GetValue(value), p.PropertyType));

    private static string Kind(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(bool))
        {
            return "bool";
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly))
        {
            return "date";
        }

        if ((type.IsPrimitive && type != typeof(char)) || type == typeof(decimal))
        {
            return "number";
        }

        return type == typeof(string) || type == typeof(char) || type == typeof(Uri) || type == typeof(Guid) || type.IsEnum ? "text" : "object";
    }
}
