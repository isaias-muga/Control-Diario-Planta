# ADR 0001: Module map: Kernel, Identidad, Recepción and a future Producción

- **Status:** accepted
- **Accepted at control 2:** see docs/modulos.md, section 4 (approval line by Isaías Muga, 2026-10-21)
- **Date:** 2026-10-21
- **Decided by:** Isaías Muga (tech lead), pending approval at control 2
- **Module(s):** company

## Context

Control Diario is one layered monolith (Domain, Application, Infrastructure, Api) with one `AppDbContext`, one migration history and one `HistorialCambios` table shared by four entities. Producción will be built as a new module connected to Recepción only by contracts, and people and agents must be able to work on a module without reading the others.

The analysis of the current code found these couplings between the identity code (Usuario, Operario, login, PIN) and the reception code (Proveedor, RecepcionLeche, calidad, silos):

1. Foreign keys from `Recepciones.RegistradoPorUsuarioId`, `Recepciones.OperarioId`, `Recepciones.AnuladaPorUsuarioId` and `Proveedores.CreadoPorUsuarioId` to `Usuarios`/`Operarios`.
2. Foreign key `HistorialCambios.UsuarioId` to `Usuarios`.
3. One `HistorialCambios` table describing four entities (`Recepcion`, `Proveedor`, `Usuario`, `Operario`).
4. `RecepcionQueries` reads `Usuarios` and `Operarios` (names) and `HistorialCambios` (correction counts).
5. `HistorialQueries` reads the four entity tables and `Usuarios`.
6. `ProveedorAppService.RegistrarRecepcionAsync` calls `VerificacionPinService` (PIN attempts live on `Usuario`).
7. `DemoDataSeeder` credits the demo rows to the first admin.
8. Application exceptions and the middleware know every exception type.
9. `CambioRegistrado` validates a fixed list of four entity names and `Campos` holds the fields of all four.
10. `Politicas`, `RolUsuario` and `IUsuarioActual` are used everywhere.

Inside the reception code, `Proveedor` and `RecepcionLeche` are tightly tied: the foreign key, the join of the list with the proveedor name, the proveedor recepción count, two proveedores loaded in every correction, and the duplicate-ticket answer that returns a proveedor name. The only "configuration" in the system is the `ConfiguracionCalidad` row (quality limits and silo count), read and written only by the reception code. There is no general configuration.

## Decision

We split the system into **Kernel** (shared types, no tables), **Identidad** (users, operarios, login, PIN: own data and screens), **Recepción** (proveedores, recepciones, calidad, silos, tickets, plant quality configuration) and a future **Producción**; `Proveedor` and `RecepcionLeche` stay in the same module; and there is no "configuración" module.

## Alternatives considered

| Option | Why not |
|---|---|
| One "Núcleo compartido" with users, roles, audit and base types | Mixes a module that owns data and screens (Identidad) with types that must stay inert. Every Identidad change would look like a change to the base types, and the base types could not be kept free of tables |
| Proveedores and Recepciones as two modules | The analysis found about six crossing edges (foreign key, list join, count, two proveedores per correction, ticket duplicate message, deactivation affecting registration). All of them would become contracts for no gain in parallel work: the same person changes both in almost every screen |
| A "Configuración" module for plant settings | The only configuration is `ConfiguracionCalidad`, used only by Recepción. A module with one consumer is a function call with extra steps. Revisit if Producción needs to edit silos (ADR 0007) |
| Keep the monolith and only add folders | Folders do not stop one module from reading another module's tables. The goal (parallel work with contracts) needs compile-time and schema boundaries |

## Consequences

- Good: the crossing edges between Identidad and Recepción drop from ten couplings of data and code to two contracts (ADR 0002); Producción starts in the final structure.
- Good: Recepción changes (the most frequent) never touch the Kernel or Identidad.
- Bad / costs: a large mechanical move of projects, namespaces and tests (milestones B1 to B4); Identidad and Kernel changes go in a chain by company rule.
- What must be enforced by a machine: module boundary architecture tests (ADR 0008); `CODEOWNERS` per module folder (ADR 0010).

## Links

`docs/modulos.md`, `docs/contratos/*.md`, `docs/plan-migracion-modular.md`, `traza-manual/docs/reloj-suizo.md` §4.
