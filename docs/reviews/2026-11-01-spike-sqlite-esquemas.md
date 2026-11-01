# Spike B0-1: SQLite with schemas (EnsureCreated, queries, ExecuteDelete)

- **Issue:** #25, milestone B0 (part 1 of 2). Spec: `docs/specs/B0-spike.md`, "Issue B0-1".
- **Date:** 2026-11-01.
- **Versions:** `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12 (EF Core 10.0.12, `Microsoft.Data.Sqlite` 10.0.12), SQLite engine 3.53.3, .NET SDK 10.0.401.
- **Method:** a throwaway console project under `artifacts/spike-b0-sqlite/` (git-ignored, not in `DairyControl.slnx`, not committed). Each experiment ran in a fresh in-memory connection. The contexts of E6 and E7 take a flag that `OnModelCreating` reads (cross-schema FK on or off, strip the schema on or off), so they use a custom `IModelCacheKeyFactory`: EF caches one model per context type and would otherwise ignore the flag on the second use. A first run without it gave E6 and E7 results that did not reflect the flag, and was discarded and rerun (E1 to E5 take no flag and need none). The versions above are the ones already used by `tests/DairyControl.Infrastructure.Tests` (`Microsoft.EntityFrameworkCore.Sqlite` 10.0.12).

## Headline

**The EF Core SQLite provider ignores the schema.** `ToTable("X", "s1")` is accepted, but the DDL and every query use the bare table name `"X"` in the `main` database. `ATTACH DATABASE` does not change that: EF still emits the unqualified name, so it never reaches the attached database. Two tables with the same name in two schemas therefore collide on a shared connection.

## Results

| # | Case | Observed result |
|---|---|---|
| E1 | One context, `A` in `s1` and `B` in `s2`, `EnsureCreated()` on `DataSource=:memory:` | `EnsureCreated()` returned `True`. Both tables created as `main.A` and `main.B`, unqualified: `CREATE TABLE "A" (...)`, `CREATE TABLE "B" (...)`. An insert and read through the schema-mapped entity works. No error. |
| E2 | Two contexts, `s1.HistorialCambios` and `s2.HistorialCambios`, one shared open connection, `EnsureCreated()` on each | First call `True`, second call `False` (it **skips** silently because the database already has tables). Only one table exists: `main.HistorialCambios`. Forcing the second creation with `IRelationalDatabaseCreator.CreateTables()` fails: `SqliteException: SQLite Error 1: 'table "HistorialCambios" already exists'.` |
| E3 | E2 after `ATTACH DATABASE ':memory:' AS s1` and `AS s2` | Same as E2: first `True`, second `False`; the only table is `main.HistorialCambios`; the script is `CREATE TABLE "HistorialCambios"`, **not** `"s1"."HistorialCambios"`. EF does not emit the schema. `CreateTables()` on each context fails with the same `table "HistorialCambios" already exists`. |
| E4 | Insert and read one row from each table with `AsNoTracking()`, `Where`, `OrderByDescending` | On a shared connection (E2/E3) the spec'd case cannot run: the setup fails as above (the plain `EnsureCreated()` path raises no error, it silently skips). The translation never uses the schema, with or without `ATTACH`: `SELECT "h"."Id", "h"."En", "h"."Valor" FROM "HistorialCambios" AS "h" WHERE "h"."Id" > 0 ORDER BY "h"."En" DESC`. **Supplementary run (not in the spec, one in-memory database per context):** it works and the tables stay separate: `s1` rows `[dos-s1, uno-s1]` (descending by date), `s2` rows `[uno-s2]`. |
| E5 | `ExecuteDeleteAsync()` on the entity of E3 and E1 | E1 (schema-mapped entity): works, removed 1 row. E3 (shared connection with `ATTACH`): cannot run, the setup fails (E3 collision). Supplementary run, one database per context: works, `ExecuteDelete` on `s1` removed 2 rows and `s2` still had 1. |
| E6 | FK inside a schema and FK between two schemas | Both accepted by `EnsureCreated()` inside **one context**: the FK is `FOREIGN KEY ("PadreId") REFERENCES "Padre" ("Id")` with no schema on either side, all three tables in `main`. An insert of a parent (`s1`) and a child (`s2`) works. SQLite does not check schemas, so this proves nothing about a real cross-schema FK. A FK between entities of two different contexts is not possible in the one-database-per-context strategy (each has its own database). |
| E7 | Strip the schema under SQLite (`SetSchema(null)` in `OnModelCreating`) | Same result as E1: both tables created in `main`, insert OK (so the customizer is redundant, the provider already drops the schema). E2 shape: the second context's `EnsureCreated()` returns `False`, one table `main.HistorialCambios` exists and **both contexts write into the same table** (`c2` sees 2 rows): the collision is silent, which is worse than an error. |

## Decisión

1. **Strategy chosen:** "one SQLite database per context" (each context gets its own open in-memory connection), because the provider ignores schemas and the other two strategies fail or hide the collision (E2, E3, E7).
2. **Fixture**, class `BaseSqliteEnMemoria<TContext>` in `tests/DairyControl.Infrastructure.Tests` (a new name: the existing tests each build their own `SqliteConnection` field and there is no shared helper); a test that needs two contexts creates one instance per context. The spike ran a close variant (see the note below the code) with two contexts mapping `HistorialCambios` to `s1` and `s2`:

   ```csharp
   using Microsoft.Data.Sqlite;
   using Microsoft.EntityFrameworkCore;

   public sealed class BaseSqliteEnMemoria<TContext> : IDisposable where TContext : DbContext
   {
       private readonly SqliteConnection _conexion = new("DataSource=:memory:");
       public TContext Contexto { get; }

       public BaseSqliteEnMemoria(Func<DbContextOptions<TContext>, TContext> crear)
       {
           _conexion.Open();               // the in-memory database lives while the connection is open
           Contexto = crear(new DbContextOptionsBuilder<TContext>().UseSqlite(_conexion).Options);
           Contexto.Database.EnsureCreated();
       }

       public void Dispose() { Contexto.Dispose(); _conexion.Dispose(); }
   }
   // Use: using var h = new BaseSqliteEnMemoria<ProduccionDbContext>(o => new ProduccionDbContext(o));
   ```

   (The variant that ran took the `SqliteConnection` in the factory; the version above takes the options so a module context needs only its `DbContextOptions<T>` constructor. The options-based form was not compiled in the spike.)
3. **What SQLite does not cover:** schemas (observed: every table lands in `main`, so a wrong schema name in `ToTable` or `MapHistory` is never detected) and, from general knowledge of SQL Server and CLAUDE.md rather than from this spike, the unique-violation numbers 2601 and 2627, the migrations history table in a schema, `rowversion` tokens and serializable transactions; a schema is therefore verified through the model (for example `GetSchema()` of the entity type, with the SQL Server provider and no connection) in a test that this spike did not run.
4. **`ExecuteDelete` and the cross-schema FK:** (a) `ExecuteDelete` works under the chosen strategy; (b) a foreign key between two contexts cannot be tested in SQLite, because each context has its own database; (c) the legacy `AppDbContext`, one context with every table, keeps its tests on a single database (the same fixture with one context), where its FK works only because SQLite ignores schemas, so the real schema-level behavior stays verified against SQL Server.
