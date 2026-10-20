using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>A report that is not saved yet, handed from the report list to the designer at <c>reports/new</c>.</summary>
public sealed class ReportDrafts
{
    private SaveReportRequest? _draft;

    public void Set(SaveReportRequest draft) => _draft = draft;

    public SaveReportRequest? Take()
    {
        var draft = _draft;
        _draft = null;
        return draft;
    }
}
