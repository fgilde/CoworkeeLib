---
title: "Geschichte von Coworkee: ein Blazor-Template seit 2021"
description: "Woher Coworkee kommt: ein Blazor-Template aus dem Jahr 2021, seit 2022 bei Symvopath in Produktion, 2026 als NuGet-Pakete veröffentlicht."
---

# Geschichte

Coworkee ist seit Oktober 2026 öffentlich und wirkt deshalb neu. Die Codebasis dahinter ist es nicht. Sie ist aus einem Blazor-Template gewachsen, das ich im September 2021 begonnen und für mich behalten habe: keine Pakete, keine Dokumentation, keine Ankündigung. Auf dem Template sind Apps entstanden, eine davon, [Symvopath](../showcase/symvopath.md), läuft seit 2022 in Produktion. 2026 wurden aus dem Template NuGet-Pakete, ein CLI, Coworkee Studio und diese Seite.

## Zeitleiste

Jedes Datum stammt aus der Git-Historie der beteiligten Repositories.

| Wann | Was passiert ist |
|---|---|
| Oktober 2019 | Ein Visual-Studio-Solution-Template für Clean Architecture auf .NET Core 3.0: Domain-, Application-, Persistence-, Web-, WPF- und Konsolenprojekt, mit SignalR und Service Bus. ([NetCoreCleanArchitectureSolutionTemplate](https://github.com/fgilde/NetCoreCleanArchitectureSolutionTemplate), erster Commit am 23. Oktober 2019) |
| September 2021 | Das Blazor-Template beginnt, auf .NET 5, Blazor WebAssembly und MudBlazor. Sein erster Commit am 22. September 2021 ist das quelloffene BlazorHero-Clean-Architecture-Template (MIT, heute [fullstackhero/blazor-starter-kit](https://github.com/fullstackhero/blazor-starter-kit)); von dort hat sich das Template Commit für Commit entfernt. |
| Herbst 2021 | Eine Theme-Auswahl (28. September), Berechtigungen pro Rolle, die einander voraussetzen können, mit einer Seite zur Verwaltung (30. September), Übersetzungen, die Administratoren pflegen und Schlüssel für Schlüssel überschreiben (Oktober und November), .NET 6 (11. November), Benachrichtigungen über SignalR an einzelne Benutzer, Gruppen oder alle mit einer Berechtigung (13. November), OData und MudBlazor.Extensions (5. Dezember), das erste Deployment nach Azure (27. Dezember). |
| Mai 2022 | Symvopath startet auf dem Template, mit .NET 6 (erste Commits am 13. und 16. Mai 2022). |
| November 2022 | .NET 7 im Template und in Symvopath; Symvopath bekommt sein Deployment auf AWS (28. November 2022). |
| Dezember 2022 | Die Solution heißt ab jetzt Coworkee (30. Dezember 2022). |
| 2023 und 2024 | .NET 8 (November 2023), Mailvorlagen (Januar 2024), dann .NET 9, ein Aspire-AppHost und Anmeldung über Keycloak (Dezember 2024). |
| 2025 | Erste Pläne, das Template in Module aufzuteilen (September 2025), .NET 10 (November 2025). |
| Oktober 2026 | Die Pakete, das CLI und Coworkee Studio werden öffentlich; Version 0.0.1.11 ist am 9. Oktober 2026 das erste getaggte Release. |

