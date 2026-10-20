using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Reporting;
using Coworkee.Contracts.Settings;

namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>The value and grouping the user switched report widgets to, kept in the user setting <see cref="TableSettings.ReportViews"/>.</summary>
/// <remarks>Without the settings module or a stored value that cannot be read, the choices last for this visit.</remarks>
public sealed class ReportViewChoices(ICoworkeeApi api)
{
    private Task<Dictionary<string, ReportViewChoice>>? _loading;
    private bool _readOnly;

    public async Task<ReportViewChoice?> GetAsync(Guid reportId, string widgetId) =>
        (await AllAsync()).GetValueOrDefault(ReportViews.Key(reportId, widgetId));

    public async Task SetAsync(Guid reportId, string widgetId, ReportViewChoice choice)
    {
        var all = await AllAsync();
        var key = ReportViews.Key(reportId, widgetId);
        if (choice.IsDefault)
        {
            all.Remove(key);
        }
        else
        {
            all[key] = choice;
        }

        if (_readOnly)
        {
            return;
        }

        try
        {
            await api.SetSettingsAsync(SettingScope.User, new Dictionary<string, string?>(StringComparer.Ordinal) { [TableSettings.ReportViews] = JsonSerializer.Serialize(all, JsonSerializerOptions.Web) });
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            _readOnly = true;
        }
    }

    private Task<Dictionary<string, ReportViewChoice>> AllAsync() => _loading ??= LoadAsync();

    private async Task<Dictionary<string, ReportViewChoice>> LoadAsync()
    {
        try
        {
            var value = ((await api.GetSettingsAsync(SettingScope.User)) ?? []).FirstOrDefault(s => s.Name == TableSettings.ReportViews)?.Value;
            return value is { Length: > 0 } ? JsonSerializer.Deserialize<Dictionary<string, ReportViewChoice>>(value, JsonSerializerOptions.Web) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            // saving now would overwrite the choices that could not be read
            _readOnly = true;
            return [];
        }
    }
}
