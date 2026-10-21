# ADR 0009: Client modules: folders with aliases, a shared UI library, lazy routes and an import boundary test

- **Status:** proposed
- **Date:** 2026-10-21
- **Decided by:** Isaías Muga (tech lead), pending approval at control 2
- **Module(s):** company (client)

## Context

The Angular client is one application with folders by kind (`pages/`, `components/`, `services/`, `models/`, `utils/`) and relative imports (no `paths` in `tsconfig`). Findings:

- Mixed files: `models/limits.ts` holds constants of Identidad (PIN, password, username), of the common reason dialog and of Recepción; `models/correcciones.ts` mixes the history type (common) with the recepción detail; `motivo-dialog` imports the proveedor name limit; `styles.scss` has `.page:has(app-resumen|app-recepciones|app-proveedor-detail|app-usuarios)`; the toolbar in `app.html` hard-codes the tabs; `components/confirmar-accion-dialog` is imported by nobody.
- `AuthService` is used by almost every page; `carga-rapida` uses `PIN_LENGTH`.
- `Resumen`, `Configuración`, `Proveedores`, `Recepciones` and `carga-rapida` are all Recepción. `login`, `cambiar-password` and `usuarios` are Identidad.
- All routes are already `loadComponent` (lazy).

## Decision

1. Folders: `client/src/app/modulos/identidad/` and `.../modulos/recepcion/` (a future `.../modulos/produccion/`), each with its own `pages/`, `components/`, `services/`, `models/` and a `<modulo>.routes.ts` loaded with `loadChildren`. `client/src/app/shell/` holds the app shell (toolbar, banner, global interceptors for the slow-request banner).
2. **Shared UI is a client library, not a backend module**: `client/projects/ui` (path alias `@ui`, consumed from source, no packaging) holding `numero-input` (`appNumero`), `motivo-dialog`, `historial-dialog`, and the pure helpers `numero-es`, `api-error`, `periodos`, `historial` (with a field-label registry that each module fills, **keyed by entity and field**: `silo`, `litros` or `nombre` can mean different things in two modules), plus the shared styles. It has no `HttpClient`, no knowledge of any module and no constants of a module.
3. **Session is Identidad's public API**: `@identidad/publico` exports `AuthService`, `Rol`, `roleGuard`, `cambioPasswordGuard`, `authInterceptor` (the app config registers it), `PIN_LENGTH` and the test helpers `tokenDePrueba`/`iniciarSesionDePrueba` (about ten specs of other modules use them), and nothing else. Other modules import Identidad only through it.
4. **Navigation and start pages are registered by each module**: a module's routes file exports its tabs and menu entries (label, path, roles, order) and the start page of each role it owns; the shell builds the toolbar from them. Today `AuthService.inicio` hard-codes `/inicio`, `/resumen` and `/recepciones`, which are Recepción routes, so Identidad would depend on Recepción; the start page per role becomes data that Recepción registers. Settings entries stay icon buttons as `CLAUDE.md` asks.
5. **Limits are split**: `@ui` keeps the reason limits; `modulos/identidad/models/limits.ts` the PIN, password and username limits; `modulos/recepcion/models/limits.ts` the rest. `PIN_LENGTH` is exported through `@identidad/publico`.
6. **Page widths** move from global `:has(app-...)` selectors to a class each page sets itself.
7. **Boundary test**: a Node script that runs in CI (not a vitest spec: Angular's unit-test builder bundles specs with esbuild for a browser-like target and may not resolve `node:fs`; prototyped in milestone C1) scans every `.ts` import and fails when a file of a module imports another module (other than `@identidad/publico`), when `@ui` imports a module, or when a module file imports another module through a relative path. It starts with an exceptions list that only shrinks.
8. `components/confirmar-accion-dialog` is deleted (unused).
9. `tsconfig.app.json`, `tsconfig.spec.json` and the `unit-test` target of `angular.json` include `projects/ui` (today they include only `src/**`, so the specs of `@ui` would not run).

## Alternatives considered

| Option | Why not |
|---|---|
| One Angular library per module, built with ng-packagr | Real packaging and build ordering for a single deployable client; extra build time and cache invalidation. Folders with aliases give the same import rule at no build cost; libraries can come later |
| Micro-frontends (module federation) | Separate builds and deployment of one product used by one team; unjustified |
| Leave the client as is and rely on the API for boundaries | The client is where modules would collide first (shared `limits.ts`, toolbar, global styles) |

## Consequences

- Good: a module owner works in one folder; `CODEOWNERS` maps folders; Producción adds `modulos/produccion/` and registers its navigation without editing other modules' files.
- Bad / costs: a large mechanical move of files and imports (milestones C1 to C3); `CLAUDE.md` client rules about folders change.
- What must be enforced by a machine: the import boundary spec (C4); the e2e suite per module; `ng build` with the alias paths.

## Links

`docs/modulos.md`, `docs/plan-migracion-modular.md` (C1 to C5).
