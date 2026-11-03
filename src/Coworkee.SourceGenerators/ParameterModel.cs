using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Coworkee.SourceGenerators;

/// <param name="Element">The element type of a collection bound from the query.</param>
/// <param name="QueryProperties">The properties of a complex type bound from the query.</param>
internal sealed record ParameterModel(IParameterSymbol Symbol, Binding Binding, string WireName, ITypeSymbol? Element, IReadOnlyList<string> QueryProperties);
