# ADR 0005: Migrations per module and how the existing tables move (two phases)

- **Status:** proposed
- **Date:** 2026-10-21
- **Decided by:** Isaías Muga (tech lead), pending approval at control 2
- **Module(s):** company (Identidad, Recepción; Producción is not affected)

## Context

Production holds real data in ten tables of the `dbo` schema, created by ten migrations of one `AppDbContext` (the latest is `20261020143152_AgregarCuentasProtegidas`). `docs/production.md` requires every migration to be backward compatible with the previous release, because the old and the new revision overlap for a moment during a deploy and the API applies migrations at startup (one replica).

Moving a table to another schema is not backward compatible: the previous revision queries `dbo.Usuarios`. Splitting `dbo.HistorialCambios` into one table per module and dropping the foreign keys between modules are data changes that production cannot take back with a redeploy of the previous SHA, because the old code does not understand the new schema.

The reception pilot has a 4-week adoption test that ends around **2026-11-17**. No schema surgery is wanted on production during it.

## Decision

We do the work in two phases.

**Fase A (now): the structure changes, no data moves.** Identidad and Recepción code moves to module projects but stays mapped to the current `dbo` tables, with one `AppDbContext` in one transitional infrastructure project that references both modules (a listed exception that only shrinks, ADR 0008). Contracts are used from the start, so the code is already in its final shape. **Producción is built in the final structure from its first day**: its own schema `produccion`, its own `ProduccionDbContext` and migration history, no foreign key and no read of another module's table. Every fase A deploy is rolled back by redeploying the previous SHA.

**Fase B (not before the adoption check): the data moves.** Three steps, each in its own deploy ("deploy solo"):

1. **Puente 1.** Migration 11 of the legacy chain: `CREATE SCHEMA identidad` and `recepcion`, then `ALTER SCHEMA ... TRANSFER` for `Usuarios`, `Operarios` (to `identidad`) and `Proveedores`, `Recepciones`, `ConfiguracionesCalidad` (to `recepcion`). Rows are not copied; indexes and constraints travel with the table. `AppDbContext` maps the new schemas in the same deploy.
2. **Per-module contexts.** `IdentidadDbContext` and `RecepcionDbContext` replace `AppDbContext` (code only, in parallel). Until the next step both map `dbo.HistorialCambios`, each reading and writing only its own entities.
3. **Puente 2.** Migration 12 in one transaction: create `identidad.HistorialCambios` and `recepcion.HistorialCambios`, copy the rows by `Entidad` (`Usuario`, `Operario` to the first; `Recepcion`, `Proveedor` to the second), compare row counts per table and `THROW` when they differ, drop `dbo.HistorialCambios`, **drop the five cross-module foreign keys** (`Recepciones.RegistradoPorUsuarioId`, `Recepciones.OperarioId`, `Recepciones.AnuladaPorUsuarioId`, `Proveedores.CreadoPorUsuarioId`, `HistorialCambios.UsuarioId`; the columns stay as plain `Guid`s), and create `identidad.__EFMigrationsHistory` and `recepcion.__EFMigrationsHistory`, each with the row of that module's `Baseline` migration. Each module chain starts with a real migration with an empty `Up` and a fixed id (the same value written in migration 12); if the ids differ, `Migrate()` would try to create tables that exist. The module models keep the indexes that the dropped foreign keys created (`HasIndex`), so the next migration does not emit a surprising `DropIndex`. Schemas are created with `EnsureSchema`, not with a loose `CREATE SCHEMA` inside `Sql()`.

The ten old migrations are not edited. The full `AppDbContext` (every mapping, unchanged) **keeps running the legacy chain until Puente 2**, even after the module code reads and writes through `IdentidadDbContext` and `RecepcionDbContext`; during that stretch the same tables are mapped twice, so each context has a test that its model equals the migrated database, and a test that the real `AppDbContext` model has no pending changes (EF Core 9 and later make `Migrate()` throw on pending model changes, so any drift would stop production at startup; that test is the first commit of milestone B1). At Puente 2 the legacy migrations, their `Designer` files, the snapshot **and** `AppDbContext` move together to a frozen "Legado" project, which is then the only project that defines `AppDbContext` (no two types with the same name in assemblies that reference each other, because the old files refer to it through the project-level `<Using>`), and its context ignores `PendingModelChangesWarning` because it is frozen. Milestone B0 confirms this behavior before anything depends on it. A fresh database runs the legacy chain (migrations 1 to 12) and then each module's chain, ending in the same state as a migrated production database. Startup order: legacy, Identidad, Recepción, then the enabled new modules, each as its own `PasoDeArranque`.

Every fase B deploy follows `docs/plan-migracion-modular.md` ("Plantilla de un hito de mudanza de datos"): a rehearsal on a restored copy of production with row counts before and after, a backup, a deploy outside loading hours with notice to the office, a short downtime, a smoke check and a written rollback. **The previous revision is taken out of service before the deploy starts**: today a deploy leaves the old revision serving until the new one is ready (`docs/production.md`), and that revision queries the old schema; it would answer errors and lose any write it accepts. The downtime is therefore planned, not a side effect of the deploy (restore the database to the point in time just before the deploy, redeploy the previous SHA, smoke check).

**Contracts do not move with the data.** `IVerificacionDeOperario`, `IDirectorioDeIdentidad` and the reserved `IRecepcionSilosQueries` have the same signature, DTOs, rules and contract tests before and after fase B. Only the implementations change their tables (`dbo.*` to `identidad.*`/`recepcion.*`). A consumer, Producción included, notices nothing.

## Alternatives considered

| Option | Why not |
|---|---|
| Do the whole move now | Schema surgery on production during the adoption test, for no benefit to the pilot. The structure work (fase A) gives the parallel work the company wants without touching the data |
| No-downtime move: synonyms in `dbo` and expand/contract over two deploys | More milestones and more temporary code. A synonym cannot cover the split history table, so the previous revision would still break at that step. The tech lead chose short, planned downtime outside loading hours |
| Copy tables into the new schemas and keep the old ones | Doubles the data, needs a sync window, and the old tables must be deleted later anyway (a delete of real data) |
| Re-baseline: new databases only, migrate data by script | Loses the proof that migrations rebuild the real schema; more room for silent data loss |

## Consequences

- Good: no production schema change during the adoption test; Producción is unblocked immediately and never needs to move.
- Good: the data moves are small, separate and each one has a rehearsal and a rollback.
- Bad / costs: during fase A the migrations of Identidad and Recepción stay in one chain, so milestones of both modules that add migrations go in a chain between them (Producción has its own chain). `AppDbContext`, five cross-module foreign keys and the shared history table remain, as listed exceptions, until fase B.
- Bad / costs: a data-move deploy has a short downtime, and anything saved between the deploy and a rollback is lost, which is why they run outside loading hours.
- Risk to check in the rehearsal: object-level permissions granted on a table are dropped by `ALTER SCHEMA ... TRANSFER`. Production connects with the SQL server admin login (`infra/production.ps1`), so the risk is low, but the rehearsal runs the app with that same login.
- What must be enforced by a machine: the exceptions list of the boundary tests (a new exception fails the build; removing one is celebrated in the PR); a migration test that builds a database from migration 1 to the last and compares it with the model of every context; the row-count `THROW` inside Puente 2.

## Links

`docs/plan-migracion-modular.md`, `docs/production.md`, ADR 0004, ADR 0006, ADR 0008, `docs/contratos/recepcion.md`.
