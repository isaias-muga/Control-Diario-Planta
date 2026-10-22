# ADR 0003: Project structure per module and composition

- **Status:** accepted
- **Accepted at control 2:** see docs/modulos.md, section 4 (approval line by Isaías Muga, 2026-10-21)
- **Date:** 2026-10-21
- **Decided by:** Isaías Muga (tech lead), pending approval at control 2
- **Module(s):** company

## Context

Today the four layer projects (`Domain`, `Application`, `Infrastructure`, `Api`) enforce the dependency rules of `CLAUDE.md` by project reference: Domain references only the BCL, Application references only Domain, and so on. Those rules are checked by reflecting on referenced assemblies, which is strong and cheap. The solution file, the Dockerfile (one `COPY` per csproj) and `Program.cs` list the projects by name.

## Decision

Each module has its own layer projects under `src/<Modulo>/`: `DairyControl.<Modulo>.Domain`, `.Application`, `.Infrastructure`, `.Api` (controllers of the module) and, when it publishes a contract, `.Contracts`. Shared types live in `src/Kernel/` (`DairyControl.Kernel`, BCL only, and `DairyControl.Kernel.Persistence`, EF Core). The host `src/DairyControl.Api` keeps `Program.cs`, the exception middleware, rate limiting, CORS, health checks and the startup steps. Tests live in `tests/<Modulo>/`, with the cross-module architecture tests staying in `tests/DairyControl.ArchitectureTests`.

Each module exposes one registration method in its `Api` project (`AddRecepcion()`, `AddIdentidad()`, `AddProduccion()`) that registers its services, its `DbContext`, its controllers (as an application part) and its startup steps. The host calls them; a module that is switched off (`Modules:<Modulo>:Enabled=false`) registers nothing and its endpoints do not exist.

Layer rules inside a module are the existing ones: Domain references only the BCL and the Kernel; Application references its Domain, the Kernel and the `Contracts` of other modules; Infrastructure references its Application and Domain, plus the Kernel persistence helpers; `Api` references its Application, its `Contracts` and the Kernel.

## Alternatives considered

| Option | Why not |
|---|---|
| One assembly per module with `internal` types and folders for layers | Simpler solution file, and `internal` hides the module. But "Domain references only the BCL" and "EF Core never in Application" can no longer be checked by assembly references, only by namespace dependency analysis, which is weaker. The existing rules are cheap to keep as they are |
| Controllers in the host | Every module owner would edit the host project, and a controller could call any module's Application service. The boundary test would have to inspect namespaces instead of references |
| Keep the layer projects shared and split by folder inside them | Gives the files a place but not a boundary: nothing stops Recepción from using an Identidad repository |

## Consequences

- Good: the dependency rules of `CLAUDE.md` survive unchanged and a module boundary is a project reference that fails the build.
- Good: `CODEOWNERS` maps one folder to one owner.
- Bad / costs: about five projects per module; `DairyControl.slnx`, the Dockerfile `COPY` lines and `tests/` grow with every module. A module template (docs/modulos.md, "Cómo se agrega un módulo") lists every place to touch.
- Transitional state (fase A, see `docs/plan-migracion-modular.md`): until the data move (fase B) the existing `DairyControl.Infrastructure` project (with `AppDbContext`) stays as the **transitional project**: milestones B3 and B4 leave it as the only project that references both modules, and B10 retires it. It is a listed exception that only shrinks.
- What must be enforced by a machine: reference rules between projects (ADR 0008); a new module project that is not in the solution or the Dockerfile fails CI (the build does).

## Links

ADR 0001, ADR 0008, ADR 0010, `docs/modulos.md`.
