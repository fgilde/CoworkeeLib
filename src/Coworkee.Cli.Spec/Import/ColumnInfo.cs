namespace Coworkee.Cli.Spec.Import;

public sealed record ColumnInfo(string Name, string Type, bool Nullable, int? MaxLength, int? Precision, int? Scale);
