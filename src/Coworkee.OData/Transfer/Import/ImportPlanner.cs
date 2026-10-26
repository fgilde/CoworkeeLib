using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Coworkee.Application.Authorization;
using Coworkee.Contracts.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Transfer.Import;

public sealed record ImportRowPlan(int Row, string Action, object? Command, IReadOnlyDictionary<string, string?> Values, List<string> Errors)
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Skip = "Skip";

    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Turns rows of a file into the import's commands without running them: converts the cells, resolves lookups and the key of
/// rows to update, starts updates from the stored row and validates every command with its FluentValidation validators.
/// </summary>
public sealed class ImportPlanner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly MethodInfo FindMethod = typeof(ImportPlanner).GetMethod(nameof(FindAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly IServiceProvider _services;
    private readonly ODataImportRegistration _import;
    private readonly ImportSettings _settings;
    private readonly IReadOnlyList<ImportField> _fields;
    private readonly ImportValueConverter _converter;
    private readonly Func<string, string> _translate;
    private readonly Type _entity;
    private readonly Dictionary<(string Set, string Text), (Guid? Id, string? Error)> _lookups = [];
    private readonly Dictionary<Type, string?> _denied = [];

    public ImportPlanner(IServiceProvider services, ODataImportRegistration import, ImportSettings settings, bool typed, CultureInfo culture, Func<string, string> translate)
    {
        _services = services;
        _import = import;
        _settings = settings;
        _translate = translate;
        var registry = services.GetRequiredService<ODataEntityRegistry>();
        _fields = ImportField.Of(import, registry);
        _entity = registry.Find(import.EntitySet)?.EntityType ?? throw new InvalidOperationException($"{import.EntitySet} is no registered OData set.");
        _converter = new ImportValueConverter(culture, typed, translate);
    }

    /// <summary>The first problem of the settings themselves, e.g. a mode without key; null when the rows can be planned.</summary>
    public string? Check()
    {
        if (_settings.Mode != ImportMode.Create && _import.Update is null)
        {
            return "This data can only be imported as new rows.";
        }

        if (_settings.Mode != ImportMode.Create && (_settings.KeyField is null || KeyColumn < 0 || KeyProperty is null))
        {
            return "Choose the key column that finds the rows to update.";
        }

        return _settings.Columns.Any(c => c is not null) ? null : "Map at least one column.";
    }

    public async Task<ImportRowPlan> PlanAsync(ImportTable table, int index, CancellationToken cancellationToken)
    {
        var cells = table.Rows[index];
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var column = 0; column < _settings.Columns.Count && column < cells.Length; column++)
        {
            if (_settings.Columns[column] is { } field)
            {
                values[field] = cells[column];
            }
        }

        var errors = new List<string>();
        var (action, existing) = await ExistingAsync(cells, errors, cancellationToken);
        if (action == ImportRowPlan.Skip)
        {
            return new ImportRowPlan(table.RowNumber(index), action, null, values, errors);
        }

        var row = await RowAsync(values, existing, errors, cancellationToken);
        object? command = null;
        if (errors.Count == 0)
        {
            try
            {
                var typed = row.Deserialize(_import.RowType, Json)!;
                command = existing is null ? _import.Create(typed) : _import.Update!(IdOf(existing), typed);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
            {
                errors.Add(exception.Message);
            }
        }

        if (command is not null)
        {
            errors.AddRange(await ValidateAsync(command, cancellationToken));
        }

        return new ImportRowPlan(table.RowNumber(index), action, command, values, errors);
    }

    private int KeyColumn => _settings.KeyField is { } key ? IndexOf(key) : -1;

    private PropertyInfo? KeyProperty => _settings.KeyField is { } key ? _entity.GetProperty(key) : null;

    private int IndexOf(string field)
    {
        for (var i = 0; i < _settings.Columns.Count; i++)
        {
            if (_settings.Columns[i] == field)
            {
                return i;
            }
        }

        return -1;
    }

    private async Task<(string Action, object? Existing)> ExistingAsync(string?[] cells, List<string> errors, CancellationToken cancellationToken)
    {
        if (_settings.Mode == ImportMode.Create)
        {
            return (ImportRowPlan.Create, null);
        }

        var key = KeyColumn < cells.Length ? cells[KeyColumn] : null;
        if (string.IsNullOrWhiteSpace(key))
        {
            if (_settings.Mode == ImportMode.Upsert)
            {
                return (ImportRowPlan.Create, null);
            }

            errors.Add(_translate("The key column is empty."));
            return (ImportRowPlan.Skip, null);
        }

        var property = KeyProperty!;
        var (value, error) = _converter.Convert(key, property.PropertyType);
        if (error is not null)
        {
            errors.Add(Format(error, key));
            return (ImportRowPlan.Skip, null);
        }

        var found = await (Task<List<object>>)FindMethod.MakeGenericMethod(_entity).Invoke(null, [_services, property, value, cancellationToken])!;
        switch (found.Count)
        {
            case 1:
                return (ImportRowPlan.Update, found[0]);
            case 0 when _settings.Mode == ImportMode.Upsert:
                return (ImportRowPlan.Create, null);
            case 0:
                errors.Add(Format("No row with {0} \"{1}\".", _translate(LabelOf(property.Name)), key));
                return (ImportRowPlan.Skip, null);
            default:
                errors.Add(Format("More than one row has {0} \"{1}\".", _translate(LabelOf(property.Name)), key));
                return (ImportRowPlan.Skip, null);
        }
    }

    private async Task<JsonObject> RowAsync(Dictionary<string, string?> values, object? existing, List<string> errors, CancellationToken cancellationToken)
    {
        var row = new JsonObject();
        foreach (var field in _fields.Where(f => !f.KeyOnly))
        {
            if (existing is not null && _entity.GetProperty(field.Name) is { } stored && ImportField.IsSimple(stored.PropertyType))
            {
                row[field.Name] = JsonSerializer.SerializeToNode(stored.GetValue(existing), Json);
            }

            if (!values.TryGetValue(field.Name, out var text))
            {
                continue;
            }

            var (value, error) = await ConvertAsync(field, text, cancellationToken);
            if (error is not null)
            {
                errors.Add($"{_translate(field.Label)}: {error}");
            }
            else if (value is not null || !field.Type.IsValueType || Nullable.GetUnderlyingType(field.Type) is not null)
            {
                // an empty cell leaves a number or flag as it is: the default for new rows, the stored value for updates
                row[field.Name] = JsonSerializer.SerializeToNode(value, Json);
            }
        }

        return row;
    }

    private async Task<(object? Value, string? Error)> ConvertAsync(ImportField field, string? text, CancellationToken cancellationToken)
    {
        if (field.Lookup is { } lookup && !string.IsNullOrWhiteSpace(text) && !Guid.TryParse(text, out _))
        {
            return await LookupAsync(lookup, field, text.Trim(), cancellationToken);
        }

        var (value, error) = _converter.Convert(text, field.Type);
        return error is null ? (value, null) : (null, Format(error, text));
    }

    private async Task<(object? Value, string? Error)> LookupAsync(Lookup lookup, ImportField field, string text, CancellationToken cancellationToken)
    {
        if (!_lookups.TryGetValue((lookup.EntitySet, text), out var found))
        {
            found = !await ODataEntityAccess.IsGrantedAsync(_services, lookup.EntityType, cancellationToken)
                ? (null, Format("You may not read {0}.", lookup.EntitySet))
                : await (Task<List<object>>)FindMethod.MakeGenericMethod(lookup.EntityType).Invoke(null, [_services, lookup.Display, text, cancellationToken])! switch
                {
                    [var single] => (IdOf(single), null),
                    [] => (null, Format("No {0} named \"{1}\".", _translate(field.Label), text)),
                    _ => (null, Format("\"{1}\" names more than one {0}.", _translate(field.Label), text)),
                };
            _lookups[(lookup.EntitySet, text)] = found;
        }

        return found.Error is null ? (found.Id, null) : (null, found.Error);
    }

    private async Task<IEnumerable<string>> ValidateAsync(object command, CancellationToken cancellationToken)
    {
        if (!_denied.TryGetValue(command.GetType(), out var denied))
        {
            var checker = _services.GetRequiredService<IPermissionChecker>();
            denied = null;
            foreach (var permission in command.GetType().GetCustomAttributes<RequiresPermissionAttribute>().Select(a => a.Permission))
            {
                if (!await checker.IsGrantedAsync(permission, cancellationToken))
                {
                    denied = Format("Permission '{0}' is required.", permission);
                    break;
                }
            }

            _denied[command.GetType()] = denied;
        }

        if (denied is not null)
        {
            return [denied];
        }

        var errors = new List<string>();
        var context = new ValidationContext<object>(command);
        foreach (var validator in _services.GetServices(typeof(IValidator<>).MakeGenericType(command.GetType())).OfType<IValidator>())
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            errors.AddRange(result.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
        }

        return errors;
    }

    private string LabelOf(string name) => _fields.FirstOrDefault(f => f.Name == name)?.Label ?? name;

    private string Format(string text, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, _translate(text), arguments);

    private static Guid IdOf(object entity) => (Guid)entity.GetType().GetProperty(ImportField.Id)!.GetValue(entity)!;

    // ponytail: one query per key and per new lookup text; batch them per page of rows if large imports get slow
    private static async Task<List<object>> FindAsync<TEntity>(IServiceProvider services, PropertyInfo property, object? value, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entity = Expression.Parameter(typeof(TEntity));
        var equals = Expression.Lambda<Func<TEntity, bool>>(Expression.Equal(Expression.Property(entity, property), Expression.Constant(value, property.PropertyType)), entity);
        var visible = await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken, unnarrowed: true);
        return [.. await visible.Where(equals).Take(2).ToListAsync(cancellationToken)];
    }
}
