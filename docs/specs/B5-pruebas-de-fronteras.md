# Technical spec: B5 Architecture tests for module boundaries

<!-- Audience: agents and tests. English. Technical milestone: there is no screen, so this spec replaces the screen spec. Same structure for B0, B1, B1b, B5 and B6. -->

- **Module:** Arquitectura (`tests/DairyControl.ArchitectureTests`, the only project the three issues touch, besides one doc-comment-free `.csproj` line)
- **Plan row:** `docs/plan-migracion-modular.md`, section 2, milestone **B5**; section 4 (row B5 and "Qué se verificó de 'B5 no necesita B3 ni B4'")
- **ADRs it implements:** ADR 0008 (all ten points), ADR 0004 (consequence: reflection test over each `DbContext` model), ADR 0010 (consequence: the `AddXxx()` entry point test belongs to B6)
- **Contract documents:** none (it reads the table "Quién usa a quién" of `docs/modulos.md`)
- **Blocked by:** B1b-3 (the previous issue of the chain: all four milestones share `DairyControl.slnx`, the `Dockerfile` and these tests). B5-2 is blocked by B5-1 and B5-3 by B5-2.
- **Unblocks:** B6.

## Goal

Replace the hard-coded four-assembly world of the architecture tests with **discovery by folder**, add the boundary rules of ADR 0008, and make every rule fail loudly if it stops looking at anything. During fase A the monolith (`DairyControl.Domain/Application/Infrastructure/Api`) is the pseudo-module **Legado**; Identidad and Recepción do not exist as projects yet (B3 and B4 create them and then add their own exception entries). A module created after these tests (Producción) accepts **no** exception.

## Issues of this milestone

| Id | Title | Blocked by |
|---|---|---|
| B5-1 | `[Arquitectura] Descubrimiento de módulos por carpeta, todos los *.Api y guarda de conteo` | B1b-3 |
| B5-2 | `[Arquitectura] Reglas de referencias, contratos y excepciones con lista de excepciones de la fase A` | B5-1 |
| B5-3 | `[Arquitectura] Reglas de persistencia, controladores y host` | B5-2 |

## Design shared by the three issues

### The discovered model (`Proyecto.cs` and a new `Modulos.cs`)

- **Projects:** every `*.csproj` under `src/` (found from the folder of `DairyControl.slnx`, as `Proyecto.Raiz` does today). The assembly of a project is the `.csproj` file name without extension (no project of the repo sets `<AssemblyName>`; the test fails if one does).
- **Modules by folder:** `src/Kernel/*` is the module `Kernel`; `src/<Nombre>/*` is the module `<Nombre>`; the four projects directly under `src/` named `DairyControl.Domain`, `DairyControl.Application`, `DairyControl.Infrastructure`, `DairyControl.Api` are the pseudo-module `Legado`.
- **Layer of a project:** the last name segment: `Domain`, `Application`, `Infrastructure`, `Api`, `Contracts`, `Persistence` (of `DairyControl.Kernel.Persistence`), `TestSupport`, or `Kernel` itself.
- **Tests:** the same model for `tests/`: `tests/Kernel/*` belongs to `Kernel`, `tests/<Nombre>/*` to `<Nombre>`, `tests/DairyControl.*Tests` to `Legado`; `tests/DairyControl.ArchitectureTests` belongs to no module and is exempt from the reference rules.
- **Pure rules:** each rule is a function over this model (projects, their references, their sources), so it can be tested with a **fake model** that has a fake module named `Falso`. **Negative control (required for every rule of B5-2 and B5-3):** a test that feeds the rule a fake model that breaks it and asserts it reports the violation, and one that feeds a clean fake model and asserts it reports none. A rule that cannot fail does not count.

### The test project

`tests/DairyControl.ArchitectureTests/DairyControl.ArchitectureTests.csproj` replaces its four explicit `ProjectReference` lines by one pattern: `<ProjectReference Include="..\..\src\**\*.csproj" />`, so a new module project never requires editing it. **To verify (the plan marks it "a verificar"):** in the PR, build once with a throwaway `src/Zzz/DairyControl.Zzz.Domain/DairyControl.Zzz.Domain.csproj` (not committed) and check that the architecture test project restores, builds and the discovery sees `Zzz`; if the pattern does not work with `dotnet build DairyControl.slnx` and `dotnet test`, the builder stops with `andon` and does not choose another mechanism.

## Issue B5-1: discovery, load every `*.Api`, count guard

- `Proyecto.cs` stops pinning `typeof(Proveedor).Assembly` and the three other assemblies as the only ones; it exposes the discovered projects and `Assembly` per project (`Assembly.Load(name)`), and the existing tests that used `Proyecto.Domain/Application/Infrastructure/Api` run over the discovered projects of the **layer** instead (so the rules also apply to a module that appears later). `FuentesTests.Proyectos` (the list of four folder names) and `FuentesTests` scanning only `src` become the scan of every `.cs` of every project of `src/` **and `tests/`** (never `bin/` or `obj/`), per project.
- The controllers rule (`AutorizacionTests`: explicit policy, no class-level `[Authorize]`, only `Login` anonymous, no `[HttpDelete]`) runs over the controllers of **every** discovered `*.Api` assembly (today `Types.InAssembly(Proyecto.Api)`).
- **Guards that make a vacuous rule fail** (constants in one file, `Esperados.cs`, with a comment of the date and what each one counts): the test fails if it finds fewer than `ModulosEsperados` modules (2 today: `Kernel`, `Legado`), fewer than `ProyectosEsperados` projects in `src/` (the number found on the day the issue is built), fewer than `ControladoresEsperados` controllers (**6 on 2026-10-23**: `Auth`, `Configuracion`, `Operarios`, `Proveedores`, `Recepciones`, `Usuarios`; the plan and ADR 0008 say "five" and the code has six, see "Decisions taken by the tech lead"), or fewer than `FuentesEsperadas` source files in `src/` and in `tests/`. Raising a minimum is allowed in the PR that adds code; lowering one needs a reason in the PR.
- No new boundary rule here: B5-1 only changes how the existing rules find their input.

Tests (all in `tests/DairyControl.ArchitectureTests`):

| # | Case | Input | Expected |
|---|---|---|---|
| D1 | Modules found | the real repo | `Kernel` and `Legado` present; `Legado` has `Domain`, `Application`, `Infrastructure`, `Api` |
| D2 | Count guards | the real repo | passes; with a fake model that has 0 controllers, the guard reports a failure |
| D3 | Every `*.Api` is loaded | a fake model with a module `Falso` that has an `Api` project holding one controller without a policy | the policy rule reports that controller (the old fixed assembly would have missed it) |
| D4 | Assembly name = file name | the real repo | no `.csproj` under `src/` or `tests/` sets `<AssemblyName>` |
| D5 | Source scan covers `tests/` | a fake source `tests/Falso/X.cs` with `DateTime.UtcNow` | the clock rule reports it |
| D6 | Existing rules still run | the real repo | each rule of `DependenciasTests`, `DominioTests`, `FuentesTests`, `AutorizacionTests` runs over at least one project (no empty rule) |

Files: touches `tests/DairyControl.ArchitectureTests/*.cs` and its `.csproj`. Does not touch `src/`, `DairyControl.slnx`, the `Dockerfile`, other test projects. Migrations: no.

**Rule change** (`CLAUDE.md`, "Testing" > "Architecture tests"): replace the first sentence (`tests/DairyControl.ArchitectureTests (NetArchTest.Rules, reflection and a scan of the src sources) fails the build when a rule of this file is broken:`) by `tests/DairyControl.ArchitectureTests (NetArchTest.Rules, reflection and a scan of the sources of `src/` and `tests/`) discovers the modules by folder (`src/Kernel/`, `src/<Modulo>/`; the four projects `DairyControl.Domain/Application/Infrastructure/Api` are the pseudo-module Legado until milestone B10), references every project of `src/` through the pattern `src/**/*.csproj`, loads every `*.Api` assembly, and fails when it finds fewer modules, projects, controllers or source files than the minimums in `Esperados.cs`, so a rename cannot make a rule vacuous. It fails the build when a rule of this file is broken:` and add to the end of the list `every rule has a test with a fake model that breaks it.`

## Issue B5-2: references, contracts, exceptions and the exceptions list

ADR 0008 points 1, 2, 5, 6, 9 and 10, as pure rules over the model.

| Rule | Statement | Applies to |
|---|---|---|
| R1 References | A project of module M references only projects of M, `DairyControl.Kernel`, `DairyControl.Kernel.Persistence` (only from an `Infrastructure` project) and the `Contracts` projects of other modules. Never another module's `Domain`, `Application`, `Infrastructure` or `Api` | `src/` |
| R1b Layers inside a module | `Domain` references only the BCL and `DairyControl.Kernel`; `Application` references its `Domain`, `DairyControl.Kernel` and `Contracts` of other modules; `Infrastructure` references its `Application`, `Domain`, `Kernel`, `Kernel.Persistence`; `Api` references its `Application`, its `Contracts`, `Kernel` (and, only for the host, the `*.Api` of each module) | `src/` |
| R2 Contracts content | A `*.Contracts` project contains only interfaces, DTO classes (public get/set properties, no methods other than the compiler's) and `ContractVersionAttribute` usage | `src/` |
| R2b Contract versions | Each interface in a `*.Contracts` project has `[ContractVersion]`; its version equals the fourth column of the row whose first cell is the backticked interface name in the table under the heading `Quién usa a quién` of `docs/modulos.md`; a project that references a `Contracts` assembly whose interface is at version `0.x` fails | `src/` |
| R5 Exceptions | Every exception type in a module's `Domain` derives from `ConflictoException` or `AccesoDenegadoDeDominioException` (the other categories are sealed and used directly) | `src/` |
| R6 Layer rules per module | The existing rules (Domain only BCL+Kernel, no EF Core/ASP.NET Core in Application, no setters on entities and value objects, no direct clock reads, no `ArgumentException` for rules, rows deleted only by the demo reset, `MarcarProtegido` only from `UsuarioAppService`) run for every module | `src/` |
| R9 Tests | The reference rules R1 apply to `tests/<Modulo>` projects too (they may also reference `DairyControl.Kernel.TestSupport` and the production projects of their own module); `tests/DairyControl.ArchitectureTests` is exempt | `tests/` |

**The exceptions list** is one file, `tests/DairyControl.ArchitectureTests/ExcepcionesDeFase.cs`, a list of entries `(Id, Regla, Descripcion, Motivo, HitoQueLaQuita, Cantidad)`. Behavior: a violation not covered by an entry fails the test; an entry whose rule no longer finds exactly `Cantidad` violations also fails (so the list only shrinks and a removal is noticed); an entry for a module created after these tests, or for the module `Produccion`, is rejected. **In B5-2 the list is empty**: with Legado as one pseudo-module there is no R1 to R9 violation to list (the entries of ADR 0008 that need two modules, the transitional project referencing both and the tests creating an audit user from Identidad's domain, appear in B3 and B4, which add them in the PR that creates the violation). The mechanism is tested with fake entries.

Tests:

| # | Case | Input (fake model) | Expected |
|---|---|---|---|
| X1 | R1 | `Falso.Application` references `Recepcion.Domain` | violation naming both projects |
| X2 | R1 clean | `Falso.Application` references `Recepcion.Contracts` and `Kernel` | none |
| X3 | R1 Kernel.Persistence | `Falso.Domain` references `Kernel.Persistence` | violation; `Falso.Infrastructure` referencing it: none |
| X4 | R2 | a `Falso.Contracts` class with a method / an interface without `[ContractVersion]` | one violation each |
| X5 | R2b | an interface `[ContractVersion("1.0.0")]` named `IFalso` with no row, a row with `1.1.0`, a row with `1.0.0`; table with the heading renamed | violation / violation / none; renamed heading: a clear failure naming the heading (rule 10) |
| X6 | R2b 0.x | `Falso.Application` references a contract at `0.1.0` | violation |
| X7 | R5 | `Falso.Domain` exception deriving from `Exception` / from `ConflictoException` | violation / none |
| X8 | R9 | `tests/Falso/Falso.Tests` references `Recepcion.Application` | violation |
| X9 | List | an entry with `Cantidad = 2` and 2 violations / 1 violation / 3 violations / an entry for `Produccion` | passes / fails (entry stale) / fails (new violation) / fails |
| X10 | Real repo | the repo as it is | all rules pass with the empty list |
| X11 | Heading read | the real `docs/modulos.md` | the table under `Quién usa a quién` is found and has its three rows, first column read as written (the Kernel row's cell has no backticks and is ignored by R2b) |

Files: touches `tests/DairyControl.ArchitectureTests/*.cs` (new `Reglas*.cs`, `ExcepcionesDeFase.cs`, adjustments to the existing files). Does not touch `src/`, `docs/`. Migrations: no.

**Rule change** (`CLAUDE.md`, "Architecture tests"): append `A project of module M references only projects of M, the Kernel, `Kernel.Persistence` (Infrastructure only) and the `*.Contracts` of other modules; a `*.Contracts` project holds only interfaces, DTO classes and the Kernel attribute, each interface has `[ContractVersion]` equal to its row under "Quién usa a quién" of `docs/modulos.md`, and a contract at 0.x is not consumed; every exception of a module's Domain derives from a Kernel category; the rules apply to `tests/` too. Known breaches are listed in `ExcepcionesDeFase.cs` with the milestone that removes each; the test fails on a new violation and on an entry that no longer applies, so the list only shrinks, and a module created after the tests accepts none.`

## Issue B5-3: persistence, controllers and host

ADR 0008 points 3, 4 and 8.

| Rule | Statement |
|---|---|
| R3 Schema | For every `DbContext` type found in an `Infrastructure` assembly, its model (built with the SQL Server provider without opening a connection, as `ModeloContraSnapshotTests` does) has every table in the schema of its module: `dbo` (or no schema) for Legado, the lower-case module name for the others (`identidad`, `recepcion`, `produccion`); and no foreign key from a table to a table in another schema |
| R3b SQL strings | No source line of a module contains another module's schema name followed by a dot (for example `recepcion.`) inside a string literal of a `FromSql*`, `ExecuteSql*`, `SqlQuery*` or migration `Sql(` call, outside comments |
| R3c Cross-module tables in Legado | For Legado only, a map `TablasPorModulo` (`Usuarios`, `Operarios` -> Identidad; `Proveedores`, `Recepciones`, `ConfiguracionesCalidad` -> Recepcion; `HistorialCambios` -> shared) lets the rule see what the monolith hides. It reports as violations the five foreign keys `Recepciones.RegistradoPorUsuarioId`, `Recepciones.OperarioId`, `Recepciones.AnuladaPorUsuarioId`, `Proveedores.CreadoPorUsuarioId`, `HistorialCambios.UsuarioId`, and the table `HistorialCambios` holding entities of two modules, and **these entries go into `ExcepcionesDeFase.cs`** (`Cantidad` 5 and 1, removed by B9) |
| R4 Controllers | A controller depends only on the `Application` and `Contracts` of its own module, the Kernel and ASP.NET Core, never on `Infrastructure`, `Domain.Interfaces`, EF Core or another module (the existing "controllers do not use the DbContext or the repositories" rule, per module) |
| R8 Host | The host (the project that holds `Program.cs`) references, among module projects, only the `*.Api` project of each module and the Kernel. **In fase A the host is the Legado Api project itself and is exempt** (it already contains the controllers), so the rule is exercised only through the fake model until B3 and B4 split it |

Tests:

| # | Case | Input | Expected |
|---|---|---|---|
| Y1 | R3 real | `AppDbContext` model | passes (all tables in `dbo`) |
| Y2 | R3 fake | a fake context mapping a `produccion` module table to schema `dbo` / to `identidad` / to `produccion` | violation / violation / none |
| Y3 | R3 FK | fake model with a foreign key from `produccion.A` to `identidad.B` | violation |
| Y4 | R3b | `FromSqlRaw("select * from identidad.Usuarios")` in `Falso` for module `Produccion`; the same text in a comment | violation / none |
| Y5 | R3c | the real `AppDbContext` | exactly 5 cross-module foreign keys and 1 shared history table, all covered by the entries of the list; removing one entry makes the test fail |
| Y6 | R4 | a fake controller that depends on `Falso.Infrastructure` / on `Recepcion.Application` from module `Falso` | violation / violation |
| Y7 | R4 real | the 6 controllers | none |
| Y8 | R8 | a fake host that references `Recepcion.Domain` / `Recepcion.Api` | violation / none |

Files: touches `tests/DairyControl.ArchitectureTests/*.cs` and `ExcepcionesDeFase.cs` (two entries). Does not touch `src/`. Migrations: no.

**Rule change** (`CLAUDE.md`, "Architecture tests"): append `In every `DbContext` model each table is in the schema of its module (`dbo` for Legado) and has no foreign key to a table of another schema; no source line of a module names another module's schema inside a SQL string; a controller uses only the Application services and Contracts of its own module; the host references only the `*.Api` project of each module and the Kernel. The five cross-module foreign keys and the shared `dbo.HistorialCambios` of the legacy `AppDbContext` are listed exceptions until milestone B9.`

## Verification (every issue)

- `dotnet build DairyControl.slnx`: zero warnings.
- `dotnet test --solution DairyControl.slnx`: all green.
- B5-1: the throwaway-project check above, reported in the PR.

## Behavior that does not change

Nothing in `src/`. The existing architecture rules keep their meaning; they only gain input.

## Decisions taken by the tech lead

1. **B5-1:** the API has **6** controllers today (the plan and ADR 0008 say 5); this spec uses 6.
2. **B5-2:** the exceptions-list `Cantidad` counts one violation per offending reference or type, as the rule reports it.
3. **B5-3:** the model of every `DbContext` found in an `Infrastructure` assembly is built with the technique of `ModeloContraSnapshotTests` (B1-2): the constructor taking `DbContextOptions<T>` with the SQL Server provider, never opened. The negative control is a fake model, so the rule is never empty while only `AppDbContext` exists.

## Open decisions

1. **B5-2, B5-3:** ADR 0008 lists exceptions that need two modules (the transitional project referencing both, tests that create an audit user from Identidad's domain). With Legado as one pseudo-module those cannot be detected now. This spec leaves them to B3 and B4 and uses `TablasPorModulo` (not in any approved document) only to make the five foreign keys and the shared history table visible. The tech lead did not answer this one: it stands as the planner's choice unless the PR review objects.
