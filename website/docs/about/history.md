---
title: "History of Coworkee: a Blazor template since 2021"
description: "Where Coworkee comes from: a Blazor template started in 2021, in production at Symvopath since 2022, published as NuGet packages in 2026."
---

# History

Coworkee went public in October 2026, so it looks new. The code base behind it is not. It grew out of a Blazor template I started in September 2021 and kept to myself: no packages, no documentation, no announcement. Apps were built on it, and one of them, [Symvopath](../showcase/symvopath.md), has run in production since 2022. In 2026 I turned the template into NuGet packages, a CLI and this site.

## Timeline

Every date comes from the git history of the repositories involved.

| When | What happened |
|---|---|
| October 2019 | A Visual Studio solution template for clean architecture on .NET Core 3.0: domain, application, persistence, web, WPF and console projects, with SignalR and a service bus. ([NetCoreCleanArchitectureSolutionTemplate](https://github.com/fgilde/NetCoreCleanArchitectureSolutionTemplate), first commit 23 October 2019) |
| September 2021 | The Blazor template starts, on .NET 5, Blazor WebAssembly and MudBlazor. Its first commit, on 22 September 2021, is the open source BlazorHero clean architecture template (MIT, today [fullstackhero/blazor-starter-kit](https://github.com/fullstackhero/blazor-starter-kit)); the template moved away from it commit by commit. |
| Autumn 2021 | A theme selector (28 September), permissions per role that can require each other, with a page to manage them (30 September), translations that administrators manage and override key by key (October and November), .NET 6 (11 November), notifications over SignalR to single users, groups or everyone with a permission (13 November), OData and MudBlazor.Extensions (5 December), the first deployment to Azure (27 December). |
| May 2022 | Symvopath starts from the template, on .NET 6 (first commits 13 and 16 May 2022). |
| November 2022 | .NET 7 in the template and in Symvopath; Symvopath gets its deployment to AWS (28 November 2022). |
| December 2022 | The solution is renamed Coworkee (30 December 2022). |
| 2023 and 2024 | .NET 8 (November 2023), mail templates (January 2024), then .NET 9, an Aspire app host and sign-in through Keycloak (December 2024). |
| 2025 | First plans to split the template into modules (September 2025), .NET 10 (November 2025). |
| October 2026 | The CoworkeeLib repository starts on 1 October 2026, the template becomes the demo app on the new packages the next day, and version 0.0.1.11 is the first tagged release on 9 October 2026. |

## What AI did

A lot. The template repository has 395 commits from September 2021 to September 2026. CoworkeeLib had more than 1,600 commits in its first five weeks, and that pace came from AI coding agents: they wrote a large part of the code, the tests and these pages, while I decided what to build, reviewed it and corrected it.

What did not come out of a prompt is what the packages are built around: permissions per role, translations managed in the running app, realtime notifications by user, group and permission, themes, the audit trail, OData tables, Aspire and Keycloak. Those were built and changed over five years in the template, most of them in production at Symvopath since 2022. The packages are new code on these ideas. Judge them by their code and tests, not by the date of the first commit.
