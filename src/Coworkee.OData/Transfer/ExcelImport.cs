using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClosedXML.Excel;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Data;
using Coworkee.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Transfer;

internal static class ExcelImport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<ImportResult> ReadAsync(Stream file, ODataImportRegistration import, IServiceProvider services, CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook(file);
        var used = workbook.Worksheet(1).RangeUsed();
        if (used is null)
        {
            return new ImportResult(0, []);
        }

        var properties = import.RowType.GetProperties().ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var columns = used.FirstRow().Cells()
            .Select(cell => properties.GetValueOrDefault(cell.GetString().Trim()))
            .ToList();
        var imported = 0;
        var errors = new List<ImportRowError>();
        foreach (var row in used.RowsUsed().Skip(1))
        {
            var error = await ImportRowAsync(row, columns, import, services, cancellationToken);
            if (error is null)
            {
                imported++;
            }
            else
            {
                errors.Add(new ImportRowError(row.RowNumber(), error));
            }
        }

        return new ImportResult(imported, errors);
    }

    private static async Task<string?> ImportRowAsync(
        IXLRangeRow row, List<PropertyInfo?> columns, ODataImportRegistration import, IServiceProvider services, CancellationToken cancellationToken)
    {
        object command;
        try
        {
            var values = new JsonObject();
            for (var column = 0; column < columns.Count; column++)
            {
                if (columns[column] is { } property && !row.Cell(column + 1).IsEmpty())
                {
                    values[property.Name] = Value(row.Cell(column + 1), property.PropertyType);
                }
            }

            command = values.Deserialize(import.RowType, Json)!;
        }
        catch (JsonException exception)
        {
            return exception.Message;
        }

        // every row in its own scope: a failed row must not leave tracked entities behind for the next one
        await using var scope = services.CreateAsyncScope();
        var result = await import.Send(scope.ServiceProvider.GetRequiredService<IDispatcher>(), command, cancellationToken);
        return result.IsSuccess ? null : Describe(result.Error);
    }

    private static JsonNode? Value(IXLCell cell, Type target)
    {
        if (target == typeof(string))
        {
            return cell.GetFormattedString();
        }

        var value = cell.Value;
        return value.Type switch
        {
            XLDataType.Boolean => value.GetBoolean(),
            XLDataType.Number when value.GetNumber() is var number && number == Math.Floor(number) && Math.Abs(number) < 1e15 => (long)number,
            XLDataType.Number => value.GetNumber(),
            XLDataType.DateTime => value.GetDateTime().ToString("o", CultureInfo.InvariantCulture),
            XLDataType.TimeSpan => value.GetTimeSpan().ToString("c", CultureInfo.InvariantCulture),
            _ => cell.GetFormattedString().Trim(),
        };
    }

    private static string Describe(Error error) =>
        error.Details is { Count: > 0 } details
            ? string.Join("; ", details.SelectMany(field => field.Value.Select(message => $"{field.Key}: {message}")))
            : error.Message;
}
