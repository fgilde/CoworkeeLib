using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Settings;

/// <summary>Validates and stores setting values of one scope; the caller saves the changes.</summary>
public sealed class SettingWriter(CoworkeeDbContext db, ISettingDefinitionManager definitions, SettingProtector protector)
{
    public async Task<Error?> WriteAsync(SettingScope scope, Guid? key, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken)
    {
        foreach (var (name, value) in values)
        {
            var definition = definitions.Find(name);
            if (definition is null || !definition.Scopes.Contains(scope))
            {
                return Error.Validation(name, $"Setting '{name}' is not defined for scope {scope}.");
            }

            if (value is not null && definition.Validate(value) is { } message)
            {
                return Error.Validation(name, message);
            }
        }

        var names = values.Keys.ToList();
        var existing = await db.Set<SettingValue>()
            .Where(s => s.Scope == scope && s.ScopeKey == key && names.Contains(s.Name))
            .ToDictionaryAsync(s => s.Name, StringComparer.Ordinal, cancellationToken);
        foreach (var (name, value) in values)
        {
            var stored = value is not null && definitions.Find(name)!.IsEncrypted ? protector.Protect(value) : value;
            if (existing.TryGetValue(name, out var row))
            {
                if (stored is null)
                {
                    db.Remove(row);
                }
                else
                {
                    row.Value = stored;
                }
            }
            else if (stored is not null)
            {
                db.Add(new SettingValue { Name = name, Scope = scope, ScopeKey = key, Value = stored });
            }
        }

        return null;
    }
}
