using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Coworkee.SourceGenerators;

/// <summary>An interface method; <see cref="HttpMethod"/> is null when it is not exposed (ignored or not callable over HTTP).</summary>
internal sealed record OperationModel(IMethodSymbol Method, string? HttpMethod, string Route, ReturnKind Return, ITypeSymbol? Result, IReadOnlyList<ParameterModel> Parameters);
