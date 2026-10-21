# ADR 0004: One database, one schema and one DbContext per module

- **Status:** proposed
- **Date:** 2026-10-21
- **Decided by:** Isaías Muga (tech lead), pending approval at control 2
- **Module(s):** company

## Context

All tables are in the default `dbo` schema, mapped by a single `AppDbContext` with a single `__EFMigrationsHistory`. Two modules adding migrations would produce one linear chain that they must serialize, and a query can join any two tables. The deployment is one API container app and one database (`ControlDiario`); production runs one replica and applies migrations at startup (`docs/production.md`). Splitting into several databases would add connection strings, backups and transactions across databases for no benefit at this size.

## Decision

We keep one physical database and give each module its own schema (`identidad`, `recepcion`, `produccion`), its own `DbContext` (`IdentidadDbContext`, `RecepcionDbContext`, `ProduccionDbContext`) that maps only tables of its own schema, and its own migrations with the history table in its schema (`[schema].[__EFMigrationsHistory]`), registered with `AddModuleDbContext<T>(connectionString, schema)` from `DairyControl.Kernel.Persistence`. A module never maps, queries or joins a table of another schema, and no raw SQL names another schema.

**Producción starts this way on its first day, whatever the state of the other two modules** (see ADR 0005 for when the existing tables move).

## Alternatives considered

| Option | Why not |
|---|---|
| One database per module | Several connection strings, backups and restore points; the data move would be a copy between databases; no real gain at this size |
| One `DbContext` with schemas per entity | Keeps one migration chain and lets any module query any table again |
| Table prefixes instead of schemas | A convention nothing can enforce, and no separate history table |

## Consequences

- Good: migrations of different modules never conflict; each module owner adds migrations in their own chain.
- Good: an architecture test can read each `DbContext` model and fail if a table is outside its schema.
- Bad / costs: no transaction can span two modules' writes with a single `SaveChanges`, so a module never writes another module's data (this already holds). Health checks register one check per context. SQLite does not have schemas, so tests of Infrastructure need a spike (milestone B0); that is why B0 comes first.
- Each module's `Database.Migrate()` is a startup step in a fixed order: legacy `AppDbContext` first (until it is retired), then Identidad, Recepción, then the others that are enabled.
- What must be enforced by a machine: a reflection test over each `DbContext` model (table schema equals the module's schema, no foreign key to another schema's table); a source scan for another schema's name inside SQL strings of a module.

## Links

ADR 0005, ADR 0006, `docs/production.md` (migrations at startup, one replica).
