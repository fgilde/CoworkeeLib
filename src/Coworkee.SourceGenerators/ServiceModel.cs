using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Coworkee.SourceGenerators;

internal sealed record ServiceModel(INamedTypeSymbol Interface, string Route, string Tag, IReadOnlyList<OperationModel> Operations);

/// <summary>An interface method; <see cref="HttpMethod"/> is null when it is not exposed (ignored or not callable over HTTP).</summary>
internal sealed record OperationModel(IMethodSymbol Method, string? HttpMethod, string Route, ReturnKind Return, ITypeSymbol? Result, IReadOnlyList<ParameterModel> Parameters);

/// <param name="Element">The element type of a collection bound from the query.</param>
/// <param name="QueryProperties">The properties of a complex type bound from the query.</param>
internal sealed record ParameterModel(IParameterSymbol Symbol, Binding Binding, string WireName, ITypeSymbol? Element, IReadOnlyList<string> QueryProperties);

internal enum Binding
{
    Route,
    Query,
    Body,
    QueryObject,
    Cancellation,
}

internal enum ReturnKind
{
    Task,
    TaskOf,
    ValueTask,
    ValueTaskOf,
}
