# ADR 0008: Architecture tests enforce module boundaries, with an exceptions list that only shrinks

- **Status:** accepted
- **Accepted at control 2:** see docs/modulos.md, section 4 (approval line by Isaías Muga, 2026-10-21)
- **Date:** 2026-10-21
- **Decided by:** Isaías Muga (tech lead), pending approval at control 2
- **Module(s):** company

## Context

`tests/DairyControl.ArchitectureTests` hard-codes four assemblies (`Proyecto.cs`), four source folders (`FuentesTests`), the path of `DemoDataSeeder.cs` and the single `HistorialCambios` table. It would not detect a module reading another module's tables, and several of its rules would stop running silently if the projects were renamed. The company rule asks for tests that fail when a module uses another without going through its contract.

## Decision

The architecture tests discover modules by folder (`src/<Modulo>/`, `src/Kernel/`) and enforce:

1. **References.** A project of module M references only projects of M, the Kernel (`DairyControl.Kernel`, and `Kernel.Persistence` from Infrastructure only) and `*.Contracts` of other modules. Never another module's Domain, Application, Infrastructure or Api.
2. **Contracts.** A `*.Contracts` project contains only interfaces, DTO classes (public get/set, no logic) and the Kernel attribute. Each contract interface has `[ContractVersion]`, and the version equals the one in the table "Quién usa a quién" of `docs/modulos.md`. A contract with version 0.x is not consumed.
3. **Persistence.** In every `DbContext` model, each table is in the schema of its module and has no foreign key to a table of another schema. No source line of a module contains another module's schema name inside a SQL string or `FromSql`.
4. **Controllers.** A controller uses only the Application services of its own module and the contracts; the existing rules (explicit policy, no class-level `[Authorize]`, no `[HttpDelete]`, only `Login` and the health endpoints anonymous) run over every module.
5. **Exceptions.** Every exception in a module's Domain derives from a Kernel category (`ReglaDeNegocioException`, `ConflictoException`, `AccesoDenegadoDeDominioException`, `DemasiadosIntentosException`).
6. **Existing layer rules, per module.** Domain references only the BCL and the Kernel; Application does not use EF Core or ASP.NET Core; entities and value objects have no setters; no direct clock reads; no `ArgumentException` for rules; rows are deleted only by the demo reset of Recepción; `MarcarProtegido` is called only by Identidad's `UsuarioAppService`.
7. **Discovery guard.** The test project references every module project through an MSBuild pattern (`src/**/*.csproj`; verified in milestone B5), loads **every** `*.Api` assembly (today `Proyecto.cs` pins the assembly of the single Api project, so controllers of a module project would silently escape the policy rules) and fails if it finds fewer modules, projects, controllers (five today) or source files than expected, so a rename cannot make a rule vacuous.
8. **The host.** The host references the `*.Api` project of each module and the Kernel, nothing else of a module (the JWT setup, the session validation, `UsuarioActualHttp` and the Identidad settings move into `AddIdentidad()`).
9. **Tests are covered too.** The reference rules apply to `tests/` as well. The one known exception of fase A: tests of Recepción that create an audit user use Identidad's domain until the foreign keys are dropped (milestone B9). Shared test helpers (`RelojFijo`, `UsuarioActualFijo`) live in `DairyControl.Kernel.TestSupport`.
10. **Contract versions versus the document.** The test reads the table under the heading "Quién usa a quién" of `docs/modulos.md`: first column the backticked contract name, fourth column the version. Renaming that heading or reordering those columns requires changing the test in the same PR.

During fase A the tests start with an **exceptions list** in one file, each entry with a reason, the milestone that removes it and a count: the transitional infrastructure project references both modules; `AppDbContext` maps tables of both modules; the five cross-module foreign keys; the shared `dbo.HistorialCambios`; `DemoDataSeeder` in the transitional project; the tests of Infrastructure on `AppDbContext`. The test fails when something violates a rule and is not on the list, **and when an entry on the list no longer applies**, so the list can only shrink. For a module created after the tests exist (Producción), no exception is accepted.

## Alternatives considered

| Option | Why not |
|---|---|
| Rely on code review and CODEOWNERS | A boundary broken by a one-line `using` goes unnoticed; the company's principle is that a machine checks rules |
| Add a third-party layering analyzer | The current NetArchTest and reflection tests already cover this, and a new dependency adds maintenance |
| No exceptions list (fix everything before enabling the tests) | Cannot be done in fase A without moving data, so the tests would arrive after the work they should guard |

## Consequences

- Good: a breach of a boundary fails the build of the PR that makes it; the list of known breaches is visible and countable.
- Bad / costs: one milestone of work (B5) before Producción can start, and the old tests change their discovery code.
- What must be enforced by a machine: this ADR is the enforcement (the tests are the deliverable of milestone B5); in the final milestone (B11) the exceptions list is empty.

## Links

`tests/DairyControl.ArchitectureTests`, ADR 0002, ADR 0003, ADR 0004, `docs/plan-migracion-modular.md`.
