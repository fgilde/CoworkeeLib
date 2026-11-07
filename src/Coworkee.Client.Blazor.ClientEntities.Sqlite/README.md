# Coworkee.Client.Blazor.ClientEntities.Sqlite

Client entities in SQLite inside the browser: the server sends the copy of an OData set as a SQLite file, a reload opens the stored file in about a second, and EF Core answers queries and facets with SQL.

Part of [Coworkee](https://framework.coworkee.de/), a modular application framework for .NET 10, Blazor and Aspire.

```bash
dotnet add package Coworkee.Client.Blazor.ClientEntities.Sqlite
dotnet workload install wasm-tools   # once per machine that builds the client
```

```csharp
builder.Services.AddCoworkeeClientEntities();
builder.Services.AddCoworkeeClientEntitiesSqlite();
```

The package turns on `WasmBuildNative` in the WebAssembly project, which links SQLite into the client. A client built without it keeps its copies in memory.

## Documentation

- [SQLite in the browser](https://framework.coworkee.de/ui/client-entities/#sqlite-in-the-browser-optional)
- [All packages](https://framework.coworkee.de/reference/packages/)

Source: [github.com/fgilde/CoworkeeLib](https://github.com/fgilde/CoworkeeLib) · License: MIT
