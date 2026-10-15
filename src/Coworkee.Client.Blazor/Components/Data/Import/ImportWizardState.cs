using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Components.Data.Import;

/// <summary>What the steps of the import wizard decided so far; the steps share one instance.</summary>
public sealed class ImportWizardState(ImportTargetDto target)
{
    public ImportTargetDto Target { get; } = target;

    public ImportSheetDto? Sheet { get; private set; }

    /// <summary>The field of each column of <see cref="Sheet"/>, null to ignore it.</summary>
    public List<string?> Columns { get; private set; } = [];

    public ImportMode Mode { get; set; }

    public string? KeyField { get; set; }

    public string? Culture { get; set; }

    public ImportParseOptions Parse { get; private set; } = new();

    public ImportPreviewDto? Preview { get; set; }

    public DataTransferJobDto? Job { get; set; }

    public bool IsText => Sheet?.Delimiter is not null;

    public IEnumerable<ImportFieldDto> MissingRequired => Target.Fields.Where(f => f.Required && !Columns.Contains(f.Name));

    public ImportSettings Settings(bool dryRun = false) =>
        new(Columns, Mode, Mode == ImportMode.Create ? null : KeyField, string.IsNullOrWhiteSpace(Culture) ? null : Culture.Trim(), Parse, dryRun);

    /// <summary>A newly read file: its suggested fields, unless the columns of the same headers were mapped already.</summary>
    public void Apply(ImportSheetDto sheet)
    {
        var previous = Mapped();
        Sheet = sheet;
        Parse = new ImportParseOptions(sheet.Delimiter, sheet.Encoding, sheet.HasHeader);
        Columns = [.. sheet.Columns.Select(c => previous.TryGetValue(c.Header, out var field) ? field : c.SuggestedField)];
        Preview = null;
    }

    public void SetColumn(int index, string? field)
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (field is not null && Columns[i] == field)
            {
                Columns[i] = null;
            }
        }

        Columns[index] = field;
        Preview = null;
    }

    /// <summary>Maps the columns whose header the profile knows and takes its mode, key and language.</summary>
    public void Apply(ImportProfileDto profile)
    {
        if (Sheet is null)
        {
            return;
        }

        Columns = [.. Sheet.Columns.Select(c => profile.Columns.TryGetValue(c.Header, out var field) && Target.Fields.Any(f => f.Name == field) ? field : null)];
        (Mode, KeyField, Culture) = (Target.CanUpdate ? profile.Mode : ImportMode.Create, profile.KeyField, profile.Culture);
        Preview = null;
    }

    public SaveImportProfileRequest Profile(string name) => new(name, Mapped(), Mode, KeyField, Culture);

    private Dictionary<string, string> Mapped() =>
        Sheet is null
            ? []
            : Sheet.Columns.Where(c => c.Index < Columns.Count && Columns[c.Index] is not null).GroupBy(c => c.Header).ToDictionary(g => g.Key, g => Columns[g.First().Index]!);
}
