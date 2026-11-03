using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Coworkee.SourceGenerators;

internal sealed record ServiceModel(INamedTypeSymbol Interface, string Route, string Tag, IReadOnlyList<OperationModel> Operations);
