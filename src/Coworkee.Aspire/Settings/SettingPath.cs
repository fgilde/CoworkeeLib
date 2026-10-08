using System.Globalization;
using System.Linq.Expressions;

namespace Coworkee.Aspire.Settings;

public static class SettingPath
{
    /// <summary>Turns <c>s =&gt; s.Coworkee.Bff.Scopes[0]</c> into the environment variable name <c>Coworkee__Bff__Scopes__0</c>.</summary>
    public static string Of(Expression<Func<CoworkeeSettings, object?>> path)
    {
        var segments = new Stack<string>();
        var node = path.Body;
        while (node is not ParameterExpression)
        {
            switch (node)
            {
                case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert:
                    node = convert.Operand;
                    break;
                case MemberExpression member:
                    segments.Push(member.Member.Name);
                    node = member.Expression!;
                    break;
                case MethodCallExpression { Method.Name: "get_Item", Arguments.Count: 1 } indexer:
                    segments.Push(Evaluate(indexer.Arguments[0]));
                    node = indexer.Object!;
                    break;
                case BinaryExpression { NodeType: ExpressionType.ArrayIndex } array:
                    segments.Push(Evaluate(array.Right));
                    node = array.Left;
                    break;
                default:
                    throw new ArgumentException($"{path} is not a setting path.", nameof(path));
            }
        }

        return string.Join("__", segments);
    }

    private static string Evaluate(Expression index) =>
        Convert.ToString(Expression.Lambda(index).Compile().DynamicInvoke(), CultureInfo.InvariantCulture)
        ?? throw new ArgumentException("A setting index must not be null.", nameof(index));
}
