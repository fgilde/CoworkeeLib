namespace Coworkee.Cli.Spec.Import;

public sealed record ForeignKeyInfo(string Name, string Column, string TargetSchema, string TargetTable, string? DeleteRule);
