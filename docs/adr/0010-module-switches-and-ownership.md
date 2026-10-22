# ADR 0010: Module switches, daily merges and ownership per folder

- **Status:** accepted
- **Accepted at control 2:** see docs/modulos.md, section 4 (approval line by Isaías Muga, 2026-10-21)
- **Date:** 2026-10-21
- **Decided by:** Isaías Muga (tech lead), pending approval at control 2
- **Module(s):** company

## Context

The company rules ask each module to merge to `main` every day behind a feature flag until it is ready, and to have `CODEOWNERS` per module. Today there is no `CODEOWNERS` and no module switch. Production runs one API image (`deploy-production.yml`) and one configuration; a merge to `main` deploys nothing and a deploy is manual (`workflow_dispatch`).

## Decision

1. A module registered by `AddXxx()` reads `Modules:Xxx:Enabled`. **Identidad and Recepción are always on** (the flag does not exist for them: the product does not work without them). **A new module defaults to `false`** in `appsettings.json`, `compose.yaml` and `infra/containerapps/*.yaml`, and is switched on per environment by configuration. A module that is off registers no services, no controllers and no `DbContext`, runs no migration and has no health check.
2. The client hides a module's routes and navigation with the same switch, delivered by the API (`GET /api/Modulos`, a new endpoint with policy `Autenticado`, not anonymous, returning the names of the enabled modules; it is added to the authorization matrix test). A route of a disabled module is not reachable and a direct URL goes to the user's start page. The client reads the list after login; a user who must change the password gets 403 `debeCambiarPassword` there, and the client treats that as "no optional modules yet" until the password is changed.
3. **A module that is off is still tested.** CI has a variant that starts the stack and the API tests with `Modules__<Modulo>__Enabled=true`: that is where the module's endpoints, its rows of the authorization matrix and its e2e run, and `FabricaDeApi` can switch modules on. `compose.yaml` and the production YAML do not mention a module until it is switched on in that environment (the default is already `false`). Without this variant the first real run of a module's migrations would be the production switch-on.
4. Each module merges to `main` daily. Nothing of a module that is off can break the others, because it registers nothing; an architecture test checks that a module's `AddXxx()` is the only entry point into the host.
5. `CODEOWNERS` assigns by folder: `/src/Kernel/`, `/src/Identidad/`, `/docs/contratos/kernel.md`, `/docs/contratos/identidad.md`, `/src/DairyControl.Api/` and `/tests/DairyControl.ArchitectureTests/` to the tech lead; `/src/Recepcion/`, `/tests/Recepcion/`, `/client/src/app/modulos/recepcion/` and its contract file to the Recepción owner; a new module adds its own lines in the same PR that creates it. `/docs/modulos.md`, `/docs/adr/` and `/docs/contratos/` belong to the tech lead.
6. Kernel and Identidad changes go in a chain, never in parallel (company rule); issues that add a migration to the legacy chain go in a chain between Identidad and Recepción until fase B ends (ADR 0005).

## Alternatives considered

| Option | Why not |
|---|---|
| A flag per feature inside a module | Out of scope: this ADR is about whole modules; a module owner may use flags inside the module |
| Deploy a separate API per module | Several images, probes and secrets for a product with one database and one team; unjustified |
| No `GET /api/Modulos`; the client uses a build-time flag | A rebuild per environment, and the demo and production images would diverge |

## Consequences

- Good: Producción can merge daily from day one without exposing anything; turning it on is a configuration change.
- Bad / costs: one small endpoint and a client service to read it; `compose.yaml` and the container app definitions carry one setting per optional module.
- What must be enforced by a machine: architecture test (module entry point is `AddXxx()`); an API test that a disabled module has no routes; the authorization matrix test (`AutorizacionTests`) includes `GET /api/Modulos`; a `CODEOWNERS` check in CI (every `src/<Modulo>/` folder has a line).

## Links

ADR 0003, ADR 0005, `docs/modulos.md`, `traza-manual/CLAUDE.md` ("Modules and contracts").
