# Technical spec: B6 Scaffolding for new modules

<!-- Audience: agents and tests. English. Technical milestone: there is no screen, so this spec replaces the screen spec. Same structure for B0, B1, B1b, B5 and B6. -->

- **Module:** Host (`src/DairyControl.Api`, `compose.yaml`, `.github/`)
- **Plan row:** `docs/plan-migracion-modular.md`, section 2, milestone **B6**; section 4 ("Cómo se prueba un módulo apagado"); section 7 (rows "Interruptores `Modules:<Modulo>:Enabled`", "Health", "`GET /api/Modulos`")
- **ADRs it implements:** ADR 0010 (switches, `GET /api/Modulos`, CI variant, `CODEOWNERS`), ADR 0004 (startup order, one health check per context), ADR 0003 (`Add<Modulo>()` is the module's only entry point)
- **Contract documents:** `docs/contratos/kernel.md`, `docs/contratos/produccion.md` ("Ground rules for the module")
- **Blocked by:** B5-3 (the previous issue of the chain: these issues edit `Program.cs`, `ConfiguracionDeArranque.cs`, `ci.yml` and the architecture tests that B5 just reworked). Inside the milestone each issue is blocked by the previous one. Also note: open issue #23 (split the e2e job in CI) edits `.github/workflows/ci.yml` and `WorkflowsTests`; B6-4 is rebased on it if it merges first.
- **Unblocks:** the first Producción issue, and C6 (the client reads `GET /api/Modulos`).

## Goal

Everything a new module needs from the host, so its owner touches only `src/<Modulo>/`, `tests/<Modulo>/`, its folders of `client/` and `e2e/`: a registration method, an off-by-default switch, a list of enabled modules for the client, its required settings checked at start, a health check for its context, startup steps in a fixed order, a CI run with the module on, and `CODEOWNERS` lines. Identidad and Recepción are always on and get no switch.

## Issues of this milestone

| Id | Title | Blocked by |
|---|---|---|
| B6-1 | `[Host] Add<Modulo>(), interruptor Modules:<Modulo>:Enabled y GET /api/Modulos` | B5-3 |
| B6-2 | `[Host] Claves de arranque por módulo y salud por contexto` | B6-1 |
| B6-3 | `[Host] Pasos de arranque en orden: legado, luego módulos` | B6-2 |
| B6-4 | `[Host] Variante de CI con el módulo encendido` | B6-3 |
| B6-5 | `[Host] CODEOWNERS base y guía "Cómo se agrega un módulo"` | B6-4 |

The tech lead answered the open decisions of this milestone; they are listed in "Decisions taken by the tech lead" at the end of this file and are used below. No contract changes: Kernel 1.0.0 and Kernel.Persistence stay as approved.

## Issue B6-1: registration, switch and `GET /api/Modulos`

- **Registration list:** the host (`Program.cs`) holds one list of optional modules, each entry `(Nombre, AddMethod)`. Today the list is empty. For each entry the host reads `Modules:<Nombre>:Enabled` with default `false` (`appsettings.json` gets no entry; the key exists only where an environment sets it) and, when true, calls the module's `Add<Nombre>()` (ADR 0003). A module that is off registers **no services, no controllers (application part), no `DbContext`, no migration step and no health check**.
- **Endpoint:** `ModulosController` in the host, `[Route("api/[controller]")]`, `GET /api/Modulos`, `[Authorize(Policy = Politicas.Autenticado)]` (not anonymous), answers 200 with the names of the enabled optional modules. Identidad and Recepción are not in the answer. With the empty list it answers an empty collection. The answer is a JSON array of strings with the module names exactly as written in the host list, for example `["Produccion"]`. A user that must change the password gets the existing 403 `debeCambiarPassword`.
- **Authorization matrix:** one new row in `tests/DairyControl.Api.Tests/AutorizacionTests.cs`: `GET /api/Modulos` allowed to `Admin`, `Oficina`, `Recepcion` and `Consulta`; without a token 401.
- **Tests:**

| # | Case | Input | Expected |
|---|---|---|---|
| M1 | Empty list | the real host | `GET /api/Modulos` with each of the four roles: 200 and no module names; no token: 401 |
| M2 | Switch off | a fake descriptor `Falso` (test only) with the key absent / `false` | its `Add` method is not called; no route of its controller exists (404); it is not in the answer |
| M3 | Switch on | the same descriptor with `Modules:Falso:Enabled=true` | its `Add` is called once, its controller answers, `Falso` is in the answer |
| M4 | Key value (parsed with `bool.TryParse`, never `GetValue<bool>`, which throws on `"abc"`) | `"true"`, `"True"`, `"false"`, `""`, `"abc"` | on, on, off, off, off (an invalid value never switches a module on) |
| M5 | Entry point | architecture test over the fake model of B5 | the host source names a module only through its `Add<Nombre>` method and its `Api` namespace; a fake host that uses another type of the module fails |
| M6 | Authorization | `AutorizacionTests` | the new row passes; the architecture rule "every action names a policy" still passes |

- **Controller count:** `ModulosController` makes 7 controllers; this issue raises `ControladoresEsperados` of `Esperados.cs` (B5-1) from 6 to 7 in the same PR.
- **Files touched:** `src/DairyControl.Api/Program.cs`, a new host file for the list and the switch, `src/DairyControl.Api/Controllers/ModulosController.cs`, `tests/DairyControl.Api.Tests/AutorizacionTests.cs`, `FabricaDeApi.cs` (an extension point to switch a fake module on), `tests/DairyControl.ArchitectureTests`. **Not touched:** `compose.yaml`, `infra/containerapps/*.yaml`, `appsettings.json` (a module that is off is not named there), `client/`, migrations.
- **Rule changes** (`CLAUDE.md`):
  - "API conventions", in the list of policies: add `GET /api/Modulos` (the names of the enabled optional modules) to the endpoints with `Autenticado` and to the matrix test.
  - "Docker / configuration flags": add `Modules:<Modulo>:Enabled` (default `false`): when `true` the host calls the module's `Add<Modulo>()`; a module that is off registers no services, controllers, `DbContext`, migration or health check. Identidad and Recepción are always on and have no switch. `compose.yaml` and `infra/containerapps/*.yaml` do not name a module until it is switched on in that environment.

## Issue B6-2: startup keys per module and health per context

- **Startup keys:** `ConfiguracionDeArranque.Validar` checks the keys of each **enabled** optional module (a module that is off adds none). A module declares its keys (name and minimum length, or "must not be empty") and the host's list entry carries them. Messages follow the existing ones: `Falta <clave>: ...` and `<clave> es demasiado corto: ...`, never a value. Logged as Critical by `ExigirValida`, one line per problem. Test case, with a fake module `Falso` that requires `Falso:Secreto` (at least 16 characters): disabled -> no problem; enabled and missing -> `Falta Falso:Secreto: debe tener al menos 16 caracteres.`; enabled and 10 characters -> `Falso:Secreto es demasiado corto: debe tener al menos 16 caracteres.`; enabled and 16 -> none; the existing four keys behave as today.
- **Health:** `/health` reports 503 when the database of **any** registered context is unreachable. Today the check is `AddDbContextCheck<AppDbContext>("database")`; each enabled module adds one check for its own context, named after the module (registered by the module's own `Add<Modulo>()`, named after the module in lower case; `AddModuleDbContext` does not register it). The 10 second timeout per check (`HealthEndpoints.CheckTimeout`) applies to every registration, including those added later: it is applied with `PostConfigure<HealthCheckServiceOptions>`, so a check added after `AddDairyControlHealthChecks` is covered (S8 fails otherwise). `/health/live` stays without any database check. A module that is off adds no check.
- **Tests:** `ConfiguracionDeArranqueTests` (cases above); `HealthEndpointTests`: with only `AppDbContext`, as today; with a fake module whose check is unhealthy, `/health` is 503 and `/health/live` 200; the timeout applies to the fake registration.
- **Files touched:** `src/DairyControl.Api/Startup/ConfiguracionDeArranque.cs`, `src/DairyControl.Api/Health/HealthEndpoints.cs`, the host list file of B6-1, the two test classes. **Not touched:** the four existing keys' text, `Program.cs` order of steps (B6-3).
- **Rule changes** (`CLAUDE.md`):
  - "Security", the sentence about `ConfiguracionDeArranque`: append `Each enabled optional module declares its own required keys and the same check covers them; a module that is off adds none.`
  - "Commands", the Health bullet: append `Every registered context adds its own check to /health; /health/live checks none.`

## Issue B6-3: startup steps in order

- `Program.cs` runs the startup steps as an ordered list, each one through `PasoDeArranque.EjecutarAsync(logger, name, step)`. **Order:** first the legacy steps, in the order and with the names they have today (`aplicar las migraciones`, `crear el primer administrador`, `marcar las cuentas protegidas`, `cargar o reiniciar los datos de demostración`), then the steps of each enabled module in the order of the host list; the last one stays `iniciar el servidor`. The legacy steps keep their conditions (`Database:ApplyMigrationsOnStartup`, `DemoData:*`). A failing step logs Critical `Falló el arranque en el paso: <name>.` and stops, as today.
- The step list lives only in the host (`src/DairyControl.Api/Startup`): the host entry of each module carries its ordered steps. No shared type is added to the Kernel (no `IModuleStartupStep` in Kernel 1.0.0).
- **Tests:** `PasoDeArranqueTests` unchanged; new `OrdenDeArranqueTests`: with two fake modules `A` and `B` enabled and the legacy steps recorded, the executed order is `legacy..., A steps..., B steps..., iniciar el servidor`; a disabled module's steps never run; a failing step of `B` logs Critical with its name, `iniciar el servidor` is not reached; the legacy step names are exactly the four above.
- **Files touched:** `src/DairyControl.Api/Program.cs`, `src/DairyControl.Api/Startup/*`, the test classes. **Not touched:** what each legacy step does, `DemoDataSeeder`.
- **Rule changes** (`CLAUDE.md`): "Security", the sentence about `PasoDeArranque`: append `Startup steps run in a fixed order: the legacy ones (migrations, first admin, protected accounts, demo data), then those of each enabled module in the order of the host list.`

## Issue B6-4: CI variant with the module on

- A CI run starts the API tests (and, when the module has them, its rows of the authorization matrix and its e2e) with `Modules__<Modulo>__Enabled=true` for each optional module. `compose.yaml` and `infra/containerapps/*.yaml` are **not** edited: the variant passes the setting to its own run only (ADR 0010 point 3).
- The workflow reads the modules to turn on from **one list** (the file `.github/ci-modulos.txt`, one module name per line); today `.github/ci-modulos.txt` is empty and the variant is a documented no-op that still reports success, so the job is never "pending". The variant is not a required check.
- `FabricaDeApi` can switch a module on (B6-1), which is how the API tests of the variant run.
- **Tests:** `WorkflowsTests`: the variant exists; it does not filter or skip at workflow level; `compose.yaml` and the production YAML contain no `Modules__` key; the list of modules of `.github/ci-modulos.txt` equals the optional modules of the host list (both are empty today, so a negative control with a fake list that has a module the other lacks must make the test fail); `Ci_RequiredChecks_AlwaysStartAndReport` still passes.
- **Files touched:** `.github/workflows/ci.yml`, `.github/ci-modulos.txt` (new, empty), `tests/DairyControl.ArchitectureTests/WorkflowsTests.cs`. **Not touched:** `deploy-production.yml`, `compose.yaml`, `infra/`.
- **Rule changes** (`CLAUDE.md`):
  - "E2E tests": append `CI also runs the API tests and the e2e of each optional module with `Modules__<Modulo>__Enabled=true` (list in `.github/ci-modulos.txt`); the stack of `compose.yaml` does not name a module until it is switched on in that environment.`
  - "Testing": none.

## Issue B6-5: `CODEOWNERS` base and the guide

- **`.github/CODEOWNERS`** with the lines of ADR 0010 point 5 that exist today, owner `@isaias-muga` (the repository owner and the owner of every module, `docs/modulos.md` section 4): `/src/Kernel/`, `/src/Identidad/`, `/docs/contratos/kernel.md`, `/docs/contratos/identidad.md`, `/src/DairyControl.Api/`, `/tests/DairyControl.ArchitectureTests/`, `/src/Recepcion/`, `/tests/Recepcion/`, `/docs/contratos/recepcion.md`, `/docs/modulos.md`, `/docs/adr/`, `/docs/contratos/`. The client and `e2e/` lines of the Recepción owner are added by milestone E2.
- **Check:** an architecture test reads `.github/CODEOWNERS` and fails when a module folder `src/<Modulo>/` has no line that starts with `/src/<Modulo>/`. Negative control with a fake folder `Falso`; the real repo passes for `Kernel` and (through `/src/DairyControl.Api/`) the Legado projects.
- **Guide:** the list "Cómo se agrega un módulo" (`docs/modulos.md`, section 11) is published as a standalone `docs/agregar-un-modulo.md` in English, updated with what exists after B1 to B6 (the Kernel types to use, `AddModuleDbContext`, `MapHistory`, the switch, the keys, the health check, the CI list, `CODEOWNERS`, the authorization matrix row, the e2e folder). `docs/modulos.md` is not edited (it is approved by the tech lead).
- **Tests:** the architecture test above; a doc test is not required.
- **Files touched:** `.github/CODEOWNERS`, `docs/agregar-un-modulo.md`, `tests/DairyControl.ArchitectureTests/*`. **Not touched:** `docs/modulos.md`, `docs/adr/`, `docs/contratos/`.
- **Rule changes** (`CLAUDE.md`):
  - "Project structure": add `.github/CODEOWNERS` (owners per folder) and `docs/agregar-un-modulo.md` (how a module is added).
  - "Git workflow": append `Every module folder under src/ has a line in .github/CODEOWNERS (an architecture test checks it); a new module adds its lines in the same PR that creates it.`

## Verification (every issue)

- `dotnet build DairyControl.slnx`: zero warnings. `dotnet test --solution DairyControl.slnx`: all green.
- B6-1 and B6-3: `docker compose up --build -d` on a fresh stack and `GET /health` and `GET /api/Modulos` through nginx; `npx playwright test roles` from `e2e/` (login still works).
- B6-4: the workflow runs on the PR; `WorkflowsTests` passes.

## Behavior that does not change

Everything a user sees: with no optional module, the API, the client, the health endpoints and the startup behave as today (same step names and order), and `GET /api/Modulos` is the only new endpoint.

## Decisions taken by the tech lead

1. **B6-1 to B6-3:** the startup steps, keys and registration list live only in the host; no shared type is added to the Kernel. If a later milestone (B3) needs `IModuleStartupStep`, it is proposed then as a MINOR contract change with the owner's `Aprobado`.
2. **B6-1:** `GET /api/Modulos` answers a JSON array of module names.
3. **B6-2:** each module's `Add<Modulo>()` registers its health check, named after the module in lower case.
4. **B6-4:** the variant's list is the file `.github/ci-modulos.txt`; empty means a documented no-op that reports success; it is not a required check.
5. **B6-5:** the `CODEOWNERS` base is every line of ADR 0010 point 5 except the client and `e2e/` ones; the guide is `docs/agregar-un-modulo.md`.

6. **B6-1, B6-4 (deviation from ADR 0010 point 1):** the ADR says a new module defaults to `false` in `appsettings.json`, `compose.yaml` and the infra YAML. This milestone names no module in any of them: the key is read with default `false`, and the CI variant (B6-4) passes the setting to its own run. Reason: no module exists yet and the files stay clean until a module is switched on in that environment.
