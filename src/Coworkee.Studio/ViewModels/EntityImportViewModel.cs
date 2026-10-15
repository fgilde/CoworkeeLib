using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec.Import;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

/// <summary>Import entities from a database, an OpenAPI document or a CSV file, with a preview before they join the spec.</summary>
public sealed partial class EntityImportViewModel(EntitiesStep step) : ObservableObject
{
    public static IReadOnlyList<string> Kinds { get; } = ["Database", "OpenAPI", "CSV"];

    public static IReadOnlyList<string> Providers { get; } = ["Automatic", "postgres", "sqlServer", "sqlite"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDatabase), nameof(Placeholder), nameof(CanBrowse))]
    public partial string Kind { get; set; } = "Database";

    [ObservableProperty]
    public partial string Source { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Module { get; set; }

    [ObservableProperty]
    public partial string? Tables { get; set; }

    [ObservableProperty]
    public partial string Provider { get; set; } = "Automatic";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult), nameof(Lines), nameof(Skipped), nameof(Summary))]
    public partial ImportResult? Result { get; set; }

    public bool IsDatabase => Kind == "Database";

    public bool CanBrowse => Kind != "Database";

    public bool HasResult => Result is not null;

    public string Placeholder => Kind switch
    {
        "Database" => "Host=localhost;Database=shop;Username=app;Password=…  or  C:\\data\\shop.db",
        "OpenAPI" => "openapi.json  or  https://localhost:7301/openapi/v1.json",
        _ => "customers.csv",
    };

    public IReadOnlyList<string> Lines => Result is { } result ? [.. result.Lines(), .. result.Enums.Select(e => $"enum {e.Name}: {string.Join(", ", e.Values)}")] : [];

    public IReadOnlyList<string> Skipped => Result?.Skipped ?? [];

    public string Summary => Result is { } result ? L.Format("{0} entities for the module {1}, {2} things skipped", result.Module.Entities.Count, result.Module.Name, result.Skipped.Count) : string.Empty;

    [RelayCommand]
    public async Task BrowseAsync()
    {
        var file = await step.Main.Dialogs.OpenFile(L.Get("Choose the file to import"), Kind switch { "OpenAPI" => ["*.json"], "CSV" => ["*.csv", "*.txt"], _ => ["*.db", "*.sqlite", "*.sqlite3"] });
        if (file is not null)
        {
            Source = file;
        }
    }

    [RelayCommand]
    public async Task PreviewAsync()
    {
        Error = null;
        Result = null;
        if (string.IsNullOrWhiteSpace(Source))
        {
            Error = L.Get("Enter a connection string, a file or an address.");
            return;
        }

        IsBusy = true;
        try
        {
            var tables = Tables?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var request = new ImportRequest(Kind switch { "Database" => ImportSource.Db, "OpenAPI" => ImportSource.OpenApi, _ => ImportSource.Csv }, Source.Trim(),
                string.IsNullOrWhiteSpace(Module) ? null : Module.Trim(), tables, Provider == "Automatic" ? null : Provider);
            Result = await Task.Run(() => EntityImporter.ImportAsync(request));
        }
        catch (ImportException exception)
        {
            Error = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void Add()
    {
        if (Result is { } result)
        {
            step.AddImported(result);
            Close();
        }
    }

    [RelayCommand]
    public void Close()
    {
        Result = null;
        Error = null;
        step.IsImportOpen = false;
    }
}
