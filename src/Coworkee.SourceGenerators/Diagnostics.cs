using Microsoft.CodeAnalysis;

namespace Coworkee.SourceGenerators;

internal static class Diagnostics
{
    private const string Category = "Coworkee.ApplicationServices";

    public static readonly DiagnosticDescriptor NotCallable = new(
        "CWK001", "Member cannot be called over HTTP",
        "'{0}' of {1} cannot be called over HTTP: {2}; mark it with [ServiceOperation(Ignore = true)] to keep it off the API",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Binding = new(
        "CWK002", "Parameter cannot be bound",
        "'{0}' of {1}: {2}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateRoute = new(
        "CWK003", "Two methods share a route",
        "'{0}' of {1} has the same route as another method: {2}; give one of them [ServiceOperation(Route = ...)]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
