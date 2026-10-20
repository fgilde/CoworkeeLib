using System.Globalization;
using System.Text.Json;
using Coworkee.Application.Ai;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;
using Coworkee.Reporting.Querying;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Reporting.Ai;

/// <summary>
/// Asks the app's AI model for a report definition over the sets the user may read, checks it like a save would and, on errors, asks again with them
/// (at most <see cref="Retries"/> times). The definition is returned, never saved.
/// </summary>
internal sealed class ReportGenerator(IServiceProvider services, ReportQueries queries, ReportDefinitionCheck check)
    : IHandler<GenerateReportDefinitionQuery, Result<ReportDefinition>>,
      IHandler<GetReportAiStatusQuery, Result<ReportAiStatusDto>>
{
    public const int Retries = 2;

    private static readonly Error Unavailable = Error.Forbidden("reports.ai_unavailable", "No AI model is configured; an administrator sets one up under AI settings.");

    public async Task<Result<ReportAiStatusDto>> HandleAsync(GetReportAiStatusQuery query, CancellationToken cancellationToken) =>
        new ReportAiStatusDto(Generator is { } generator && await generator.IsAvailableAsync(cancellationToken));

    public async Task<Result<ReportDefinition>> HandleAsync(GenerateReportDefinitionQuery query, CancellationToken cancellationToken)
    {
        if (Generator is not { } generator || !await generator.IsAvailableAsync(cancellationToken))
        {
            return Unavailable;
        }

        var instructions = ReportPrompt.Instructions(await queries.SourcesAsync(cancellationToken), CultureInfo.CurrentUICulture);
        var first = ReportPrompt.Input(query.Prompt, query.Current);
        var input = first;
        IReadOnlyList<string> errors = [];
        for (var attempt = 0; attempt <= Retries; attempt++)
        {
            if (await generator.GenerateAsync(instructions, input, cancellationToken) is not { } answer)
            {
                return Unavailable;
            }

            (var definition, errors) = await ReadAsync(answer, cancellationToken);
            if (definition is not null)
            {
                return definition;
            }

            input = ReportPrompt.Retry(first, answer, errors);
        }

        return Error.Validation(nameof(query.Prompt), $"The AI model gave no usable report after {Retries + 1} tries: {string.Join("; ", errors)}");
    }

    private IAiTextGenerator? Generator => services.GetService<IAiTextGenerator>();

    private async Task<(ReportDefinition? Definition, IReadOnlyList<string> Errors)> ReadAsync(string answer, CancellationToken cancellationToken)
    {
        ReportDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<ReportDefinition>(Json(answer), ReportHandlers.Json);
        }
        catch (JsonException exception)
        {
            return (null, [$"The answer is no ReportDefinition JSON: {exception.Message}"]);
        }

        if (definition is null)
        {
            return (null, ["The answer is empty."]);
        }

        Normalize(definition);
        var request = new SaveReportRequest("AI", null, null, ReportVisibility.Private, [], definition);
        var invalid = new SaveReportValidator().Validate(request).Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}").ToList();
        if (invalid.Count == 0)
        {
            invalid.AddRange((await check.WidgetsAsync(definition, stopAtFirst: false, cancellationToken)).Select(Text));
        }

        return invalid.Count == 0 ? (definition, []) : (null, invalid);
    }

    /// <summary>The JSON object of the answer, also inside a ```json fence or after some words.</summary>
    internal static string Json(string answer)
    {
        var start = answer.IndexOf('{', StringComparison.Ordinal);
        var end = answer.LastIndexOf('}');
        return start >= 0 && end > start ? answer[start..(end + 1)] : answer;
    }

    // models leave out lists and ids; a missing list is no reason to ask again
    private static void Normalize(ReportDefinition definition)
    {
        definition.Widgets ??= [];
        definition.Filters ??= [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var widget in definition.Widgets)
        {
            if (string.IsNullOrWhiteSpace(widget.Id) || widget.Id.Length > 40 || !ids.Add(widget.Id))
            {
                widget.Id = new ReportWidget().Id;
                ids.Add(widget.Id);
            }

            widget.Options ??= new WidgetOptions();
            widget.Options.Metrics ??= [];
            widget.Options.Buckets ??= [];
            widget.Options.Thresholds ??= [];
            widget.Options.Palette ??= [];
            widget.W = Math.Clamp(widget.W, 1, ReportDefinition.GridColumns);
            widget.X = Math.Clamp(widget.X, 0, ReportDefinition.GridColumns - widget.W);
            if (widget.Query is { } query)
            {
                query.Aggregates ??= [new ReportAggregate()];
                query.Columns ??= [];
                query.Facets ??= [];
            }
        }
    }

    private static string Text(Error error) =>
        error.Details is { Count: > 0 } details ? string.Join("; ", details.Select(d => $"{d.Key}: {string.Join(' ', d.Value)}")) : error.Message;
}
