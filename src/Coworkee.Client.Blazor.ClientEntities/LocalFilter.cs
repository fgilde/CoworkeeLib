using System.Globalization;
using Microsoft.OData.UriParser;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>
/// Evaluates an OData filter on the columns of a local table, one mask over all rows per term. Comparisons, "in" and string
/// functions on a property work on the column's sorted values; anything else runs the compiled expression row by row.
/// </summary>
internal static class LocalFilter
{
    public static bool[] Evaluate<T>(LocalTable<T> table, QueryToken token) => token switch
    {
        BinaryOperatorToken { OperatorKind: BinaryOperatorKind.And or BinaryOperatorKind.Or } logical =>
            Combine(Evaluate(table, logical.Left), Evaluate(table, logical.Right), logical.OperatorKind == BinaryOperatorKind.And),
        UnaryOperatorToken { OperatorKind: UnaryOperatorKind.Not } not => Invert(Evaluate(table, not.Operand)),
        LiteralToken { Value: bool value } => Enumerable.Repeat(value, table.Count).ToArray(),
        BinaryOperatorToken { Left: PathToken path, Right: LiteralToken literal } compare when IsComparison(compare.OperatorKind) =>
            Compare(table, token, path, compare.OperatorKind, literal.Value),
        BinaryOperatorToken { Left: LiteralToken literal, Right: PathToken path } compare when IsComparison(compare.OperatorKind) =>
            Compare(table, token, path, Flip(compare.OperatorKind), literal.Value),
        InToken { Left: PathToken path } @in => In(table, path, @in),
        FunctionCallToken { Name: "contains" or "startswith" or "endswith" } function when Text(function) is var (path, casing, text) && path is not null =>
            Column(table, path).Rows(Column(table, path).Text(function.Name, casing, text!)),
        _ => RowByRow(table, token),
    };

    private static bool[] Compare<T>(LocalTable<T> table, QueryToken token, PathToken path, BinaryOperatorKind op, object? value)
    {
        var column = Column(table, path);
        var literal = ODataLinq.Literal(value, column.ValueType);
        // a property of int compared with 4.5 cannot use the column's values
        return Exact(literal, value) ? column.Rows(column.Compare(op, literal)) : RowByRow(table, token);
    }

    private static bool[] In<T>(LocalTable<T> table, PathToken path, InToken token)
    {
        var column = Column(table, path);
        return column.Rows(column.In(ODataLinq.Literals(token, column.ValueType)));
    }

    // contains(Path,'x'), contains(tolower(Path),'x') and their siblings
    private static (PathToken? Path, string? Casing, string? Text) Text(FunctionCallToken function)
    {
        var args = function.Arguments.Select(a => a.ValueToken).ToList();
        if (args is not [var target, LiteralToken { Value: string text }])
        {
            return default;
        }

        return target switch
        {
            PathToken path => (path, null, text),
            FunctionCallToken { Name: "tolower" or "toupper" } casing when casing.Arguments.SingleOrDefault()?.ValueToken is PathToken path => (path, casing.Name, text),
            _ => default,
        };
    }

    private static Column Column<T>(LocalTable<T> table, PathToken path) => table.Column(ODataLinq.PathText(path));

    private static bool[] RowByRow<T>(LocalTable<T> table, QueryToken token)
    {
        var predicate = ODataLinq.Filter<T>(token).Compile();
        var rows = table.All();
        var mask = new bool[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            mask[i] = predicate(rows[i]);
        }

        return mask;
    }

    private static bool Exact(object? literal, object? value) =>
        literal is null || value is null || !IsNumber(value) || !IsNumber(literal)
        || Convert.ToDecimal(literal, CultureInfo.InvariantCulture) == Convert.ToDecimal(value, CultureInfo.InvariantCulture);

    private static bool IsNumber(object value) => value is byte or short or int or long or float or double or decimal;

    private static bool IsComparison(BinaryOperatorKind op) => op is BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual or BinaryOperatorKind.GreaterThan
        or BinaryOperatorKind.GreaterThanOrEqual or BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual;

    private static BinaryOperatorKind Flip(BinaryOperatorKind op) => op switch
    {
        BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThan,
        BinaryOperatorKind.GreaterThanOrEqual => BinaryOperatorKind.LessThanOrEqual,
        BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThan,
        BinaryOperatorKind.LessThanOrEqual => BinaryOperatorKind.GreaterThanOrEqual,
        _ => op,
    };

    private static bool[] Combine(bool[] left, bool[] right, bool both)
    {
        var mask = new bool[left.Length];
        for (var i = 0; i < mask.Length; i++)
        {
            mask[i] = both ? left[i] && right[i] : left[i] || right[i];
        }

        return mask;
    }

    private static bool[] Invert(bool[] mask)
    {
        var inverted = new bool[mask.Length];
        for (var i = 0; i < mask.Length; i++)
        {
            inverted[i] = !mask[i];
        }

        return inverted;
    }
}
