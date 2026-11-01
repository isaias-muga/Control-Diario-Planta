# Spike B0-1: SQLite with schemas (EnsureCreated, queries, ExecuteDelete)

- **Issue:** #25, milestone B0 (part 1 of 2). Spec: `docs/specs/B0-spike.md`, "Issue B0-1".
- **Date:** 2026-11-01.
- **Versions:** `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12 (EF Core 10.0.12, `Microsoft.Data.Sqlite` 10.0.12), SQLite engine 3.53.3, .NET SDK 10.0.401.
- **Method:** a throwaway console project under `artifacts/spike-b0-sqlite/` (git-ignored, not in `DairyControl.slnx`, not committed). Each experiment ran in a fresh in-memory connection. Contexts that differ only by a flag use a custom `IModelCacheKeyFactory`, because EF caches one model per context type and a flag read in `OnModelCreating` would otherwise be ignored on the second use (a first run without it gave wrong E6 and E7 results and was discarded).

## Headline

**The EF Core SQLite provider ignores the schema.** `ToTable("X", "s1")` is accepted, but the DDL and every query use the bare table name `"X"` in the `main` database. `ATTACH DATABASE` does not change that: EF still emits the unqualified name, so it never reaches the attached database. Two tables with the same name in two schemas therefore collide on a shared connection.

## Results

| # | Case | Observed result |
|---|---|---|
| E1 | One context, `A` in `s1` and `B` in `s2`, `EnsureCreated()` on `DataSource=:memory:` | `EnsureCreated()` returned `True`. Both tables created as `main.A` and `main.B`, unqualified: `CREATE TABLE "A" (...)`, `CREATE TABLE "B" (...)`. An insert and read through the schema-mapped entity works. No error. |
| E2 | Two contexts, `s1.HistorialCambios` and `s2.HistorialCambios`, one shared open connection, `EnsureCreated()` on each | First call `True`, second call `False` (it **skips** silently because the database already has tables). Only one table exists: `main.HistorialCambios`. Forcing the second creation with `IRelationalDatabaseCreator.CreateTables()` fails: `SqliteException: SQLite Error 1: 'table "HistorialCambios" already exists'.` |
| E3 | E2 after `ATTACH DATABASE ':memory:' AS s1` and `AS s2` | Same as E2: first `True`, second `False`; the only table is `main.HistorialCambios`; the script is `CREATE TABLE "HistorialCambios"`, **not** `"s1"."HistorialCambios"`. EF does not emit the schema. `CreateTables()` on each context fails with the same `table "HistorialCambios" already exists`. |
| E4 | Insert and read one row from each table with `AsNoTracking()`, `Where`, `OrderByDescending` | On a shared connection (E2/E3) it cannot run: the setup fails as above. The translation never uses the schema, with or without `ATTACH`: `SELECT "h"."Id", "h"."En", "h"."Valor" FROM "HistorialCambios" AS "h" WHERE "h"."Id" > 0 ORDER BY "h"."En" DESC`. With **one database per context** it works and the tables stay separate: `s1` rows `[dos-s1, uno-s1]` (descending by date), `s2` rows `[uno-s2]`. |
| E5 | `ExecuteDeleteAsync()` on the entity of E3 and E1 | E1 (schema-mapped entity): works, removed 1 row. E3 (shared connection with `ATTACH`): cannot run, the setup fails (E3 collision). With one database per context: works, `ExecuteDelete` on `s1` removed 2 rows and `s2` still had 1. |
| E6 | FK inside a schema and FK between two schemas | Both accepted by `EnsureCreated()` inside **one context**: the FK is `FOREIGN KEY ("PadreId") REFERENCES "Padre" ("Id")` with no schema on either side, all three tables in `main`. An insert of a parent (`s1`) and a child (`s2`) works. SQLite does not check schemas, so this proves nothing about a real cross-schema FK. A FK between entities of two different contexts is not possible in the one-database-per-context strategy (each has its own database). |
| E7 | Strip the schema under SQLite (`SetSchema(null)` in `OnModelCreating`) | Same result as E1: both tables created in `main`, insert OK (so the customizer is redundant, the provider already drops the schema). E2 shape: the second context's `EnsureCreated()` returns `False`, one table `main.HistorialCambios` exists and **both contexts write into the same table** (`c2` sees 2 rows): the collision is silent, which is worse than an error. |

## Decisión

1. **Strategy chosen:** "one SQLite database per context": each context gets its own open in-memory connection (`DataSource=:memory:`). "ATTACH DATABASE per schema on one shared connection" and "strip the schema" do not work or hide the collision (E2, E3, E7), because the provider ignores the schema.
2. **Fixture** (in `tests/DairyControl.Infrastructure.Tests`, class `BaseSqliteEnMemoria<TContext>`, used as `new BaseSqliteEnMemoria<ProduccionDbContext>(con => new ProduccionDbContext(new DbContextOptionsBuilder<ProduccionDbContext>().UseSqlite(con).Options))`; verified in the spike with `C1` and `C2`, each with its own database):

   ```csharp
   public sealed class BaseSqliteEnMemoria<TContext> : IDisposable where TContext : DbContext
   {
       private readonly SqliteConnection _conexion = new("DataSource=:memory:");
       public TContext Contexto { get; }

       public BaseSqliteEnMemoria(Func<SqliteConnection, TContext> crear)
       {
           _conexion.Open();               // the in-memory database lives while the connection is open
           Contexto = crear(_conexion);
           Contexto.Database.EnsureCreated();
       }

       public void Dispose() { Contexto.Dispose(); _conexion.Dispose(); }
   }
   ```

3. **What SQLite does not cover:** the schema itself (a wrong or missing schema name in `ToTable`/`MapHistory` is not detected, every table lands in `main`), schema-qualified SQL and the default `dbo`, two tables with the same name in different schemas (they collide on one connection), a foreign key between schemas or between two contexts, the migrations history table in a schema, the unique-violation error numbers 2601 and 2627, column types and collations of SQL Server, `rowversion` concurrency tokens, and serializable transactions. The CLAUDE.md sentence "SQLite tests don't cover SQL Server-specific behavior" can add: "nor schemas: the SQLite provider ignores them, so a context of a module is tested on its own in-memory database and its schema is checked only through the model with the SQL Server provider".
4. **`ExecuteDelete` and the cross-schema FK:** `ExecuteDelete` works under the chosen strategy (removed 2 rows in `s1`, `s2` untouched, E4/E5). A foreign key inside one context works, but the "cross-schema" one only works because SQLite drops the schema; between two contexts in different databases it does not exist. The legacy exception (one `AppDbContext` with all tables) keeps working in SQLite as one context, and its schema-level correctness must be verified against SQL Server.
