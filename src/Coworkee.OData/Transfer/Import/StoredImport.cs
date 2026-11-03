using Coworkee.Contracts.Data;

namespace Coworkee.OData.Transfer.Import;

public sealed record StoredImport(ImportSettings Settings, string UiCulture);
