namespace Coworkee.Contracts.Data;

/// <summary>
/// The first <see cref="Checked"/> of <see cref="Total"/> rows planned and validated (a dry run checks all of them):
/// <see cref="Rows"/> are the first of them, <see cref="Invalid"/> the first that fail.
/// </summary>
public sealed record ImportPreviewDto(int Total, int Checked, int Valid, int Creates, int Updates, IReadOnlyList<ImportPreviewRowDto> Rows, IReadOnlyList<ImportPreviewRowDto> Invalid);
