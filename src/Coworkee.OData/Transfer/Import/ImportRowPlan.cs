namespace Coworkee.OData.Transfer.Import;

public sealed record ImportRowPlan(int Row, string Action, object? Command, IReadOnlyDictionary<string, string?> Values, List<string> Errors)
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Skip = "Skip";

    public bool IsValid => Errors.Count == 0;
}
