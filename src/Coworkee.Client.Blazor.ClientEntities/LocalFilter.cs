using System.Globalization;
using Microsoft.OData.UriParser;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>
/// Evaluates an OData filter on the columns of a local table, one mask over all rows per term. Terms on one property (comparisons, "in",
/// "has", string functions and their combinations) work on the column's sorted values; anything else runs the compiled expression row by row.
/// </summary>
internal static class LocalFilter
{
    public static bool[] Evaluate<T>(LocalTable<T> table, QueryToken token) => token switch
    {
        _ when OnColumn(table, token) is { } single => single.Column.Rows(single.Ranks),
        BinaryOperatorToken { OperatorKind: BinaryOperatorKind.And or BinaryOperatorKind.Or } logical =>
            Combine(Evaluate(table, logical.Left), Evaluate(table, logical.Right), logical.OperatorKind == BinaryOperatorKind.And),
        UnaryOperatorToken { OperatorKind: UnaryOperatorKind.Not } not => Invert(Evaluate(table, not.Operand)),
        LiteralToken { Value: bool value } => Enumerable.Repeat(value, table.Count).ToArray(),
        _ => RowByRow(table, token),
    };

    /// <summary>
    /// The ranks of one column that a filter on that column alone accepts, e.g. a range facet "(Total ge 5 and Total lt 10)" or "Options has 'Express'":
    /// decided once per distinct value instead of per row. Null when the filter touches several paths or needs more than the column's values.
    /// </summary>
    public static (Column Column, RankSet Ranks)? OnColumn<T>(LocalTable<T> table, QueryToken token)
    {
        switch (token)
        {
            case BinaryOperatorToken { OperatorKind: BinaryOperatorKind.And or BinaryOperatorKind.Or } logical:
                return OnColumn(table, logical.Left) is { } left && OnColumn(table, logical.Right) is { } right && left.Column == right.Column
                    ? (left.Column, logical.OperatorKind == BinaryOperatorKind.And ? left.Ranks.And(right.Ranks, left.Column.Distinct) : left.Ranks.Or(right.Ranks, left.Column.Distinct))
                    : null;
            case UnaryOperatorToken { OperatorKind: UnaryOperatorKind.Not } not:
                return OnColumn(table, not.Operand) is { } operand ? (operand.Column, operand.Ranks.Not(operand.Column.Distinct)) : null;
            case BinaryOperatorToken { Left: PathToken path, Right: LiteralToken literal } compare when IsComparison(compare.OperatorKind):
                return Ranks(table, path, compare.OperatorKind, literal.Value);
            case BinaryOperatorToken { Left: LiteralToken literal, Right: PathToken path } compare when IsComparison(compare.OperatorKind):
                return Ranks(table, path, Flip(compare.OperatorKind), literal.Value);
            case BinaryOperatorToken { OperatorKind: BinaryOperatorKind.Has, Left: PathToken path, Right: LiteralToken literal }:
                var flags = Column(table, path);
                return (flags, RankSet.Of(flags.Has(ODataLinq.Literal(literal.Value, flags.ValueType)!)));
            case InToken { Left: PathToken path } @in:
                var column = Column(table, path);
                return (column, RankSet.Of(column.In(ODataLinq.Literals(@in, column.ValueType))));
            case FunctionCallToken { Name: "contains" or "startswith" or "endswith" } function when Text(function) is var (text, casing, value) && text is not null:
                var texts = Column(table, text);
                return (texts, RankSet.Of(texts.Text(function.Name, casing, value!)));
            default:
                return null;
        }
    }

    private static (Column Column, RankSet Ranks)? Ranks<T>(LocalTable<T> table, PathToken path, BinaryOperatorKind op, object? value)
    {
        var column = Column(table, path);
        var literal = ODataLinq.Literal(value, column.ValueType);
        // a property of int compared with 4.5 cannot use the column's values
        return Exact(literal, value) ? (column, column.Compare(op, literal)) : null;
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
