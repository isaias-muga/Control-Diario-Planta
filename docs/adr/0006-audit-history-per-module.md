# ADR 0006: Audit columns and change history belong to each module; no foreign keys between module schemas

- **Status:** proposed
- **Date:** 2026-10-21
- **Decided by:** Isaías Muga (tech lead), pending approval at control 2
- **Module(s):** Kernel, Identidad, Recepción, Producción

## Context

`CLAUDE.md` requires that every correction, void and deactivation writes a `CambioRegistrado` in the same `SaveChanges` as the change, that the history is append-only, and that the audit columns (`RegistradoPorUsuarioId`, `OperarioId`, `AnuladaPorUsuarioId`, `CreadoPorUsuarioId`) reference `Usuario`/`Operario` by id. Today there is one `HistorialCambios` table for four entities and five foreign keys to `Usuarios`/`Operarios`. Users and operarios are never deleted.

The tech lead's rule: other modules reference users and operarios only by id plus Identidad's contracts, with no foreign key across module schemas.

## Decision

1. The **type** `CambioRegistrado` lives in the Kernel (shape and validation only). The Kernel no longer lists entity names or field names; each module owns its constants (`RecepcionCampos`, `IdentidadCampos`).
2. The **table** is per module: `recepcion.HistorialCambios` (entities `Recepcion`, `Proveedor`), `identidad.HistorialCambios` (entities `Usuario`, `Operario`), `produccion.HistorialCambios` for Producción. Each is mapped by `HistoryMapping.MapHistory(schema)` from `DairyControl.Kernel.Persistence`, and each module's repositories save the history in the same `SaveChanges` as the change, as today.
3. The **reader** is one `IHistorialQueries` per module, built on `HistorialQueriesBase`. The routes (`GET /api/Recepciones/{id}/historial`, `.../Proveedores/...`, `.../Usuarios/...`, `.../Operarios/...`) and their policies do not change. The author names come from `IDirectorioDeIdentidad`.
4. The **audit columns** stay in the module that owns the record, as plain `Guid` columns. The foreign keys to `Usuarios`/`Operarios` are dropped (five of them, ADR 0005, Puente 2). Names are asked from `IDirectorioDeIdentidad`; "who registered" is stamped from `IUsuarioActual` and `IVerificacionDeOperario`.
5. **Integrity** that the foreign keys gave is kept by policy and by a test: users and operarios are never deleted (`Usuario`/`Operario` have no delete code, an architecture test already forbids `Remove`), so an id written in an audit column always resolves. A contract test of Identidad checks that the directory returns deactivated operarios and users.
6. Correction counts, which `RecepcionQueries` reads from `HistorialCambios` today, are read from `recepcion.HistorialCambios` (same module).
7. The demo reset keeps clearing the Recepción history of recepciones and proveedores. After the data move, Identidad's history is in another schema and the old sentence "never touches the history of usuarios and operarios" would be trivially true. The architecture test that reads `DemoDataSeeder` moves with the seeder to Recepción and then checks the rule that matters: the Recepción seeder never names the `identidad` schema or an Identidad type. Until then (fase A) it keeps checking the current two sentences.

## Alternatives considered

| Option | Why not |
|---|---|
| Keep one shared `HistorialCambios` owned by the Kernel | Every module writes a table that is not its own schema, so `SaveChanges` atomicity needs one context for all modules again, or a shared transaction between contexts |
| Keep the foreign keys to `Usuarios` | A physical dependency on Identidad's tables: migration order, mapping of `Usuario` in every context, and a table that cannot be moved or renamed without touching other modules |
| Soft references plus a nightly integrity check | Not needed: records are never deleted, so the check could never find anything |
| One audit service that every module calls to write history | Breaks "same `SaveChanges`" (two saves) or needs a distributed transaction |

## Consequences

- Good: a module's change and its history are one transaction in one context; no module writes another module's table.
- Bad / costs: there is no single table to ask "everything that happened today". No screen needs it today; if one appears, it is a read model fed by each module's history reader (a new contract), not a table read.
- Bad / costs: nothing at the database level stops a bad `Guid` in an audit column; the application stamps it from the token, so the only way to write a bad one is a bug, caught by tests.
- What must be enforced by a machine: no foreign key between tables of different schemas (model reflection test); the architecture tests of `CLAUDE.md` about append-only history and the demo reset; the Identidad directory contract tests.

## Links

ADR 0004, ADR 0005, `docs/contratos/kernel.md`, `docs/contratos/identidad.md`.
