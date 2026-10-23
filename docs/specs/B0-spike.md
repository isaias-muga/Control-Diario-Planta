# Technical spec: B0 Spike (SQLite with schemas, `Migrate()` and a duplicated `AppDbContext`)

<!-- Audience: agents and tests. English. Technical milestone: there is no screen, so this spec replaces the screen spec. Same structure for B0, B1, B1b, B5 and B6. -->

- **Module:** Spike (no module, no behavior change)
- **Plan row:** `docs/plan-migracion-modular.md`, section 2, milestone **B0**; section 4 (critical path); section 7 (hidden dependencies: "Spike de SQLite con esquemas", "`EF Core Migrate()` falla con cambios pendientes del modelo")
- **ADRs it implements:** ADR 0004 (consequence: "SQLite does not have schemas ... milestone B0"), ADR 0005 (the frozen `Legado` context ignores `PendingModelChangesWarning`: "a confirmar en B0")
- **Contract documents:** none
- **Blocked by:** nothing. Runs in parallel with B1.
- **Unblocks:** B1b (it needs the SQLite decision), and the design of B9/B10 (it needs the other two answers).

## Goal

Answer, with evidence, three questions that the rest of the plan assumes. The deliverable is **two written reports** in `docs/reviews/`, each ending in a section named exactly `Decisión` with the answers listed below. No `src/` or `tests/` file changes, and no throwaway code is committed.

## Issues of this milestone

### Issue B0-1: SQLite with schemas

- **Title:** `[Spike] SQLite con esquemas (EnsureCreated, consultas, ExecuteDelete)`
- **Report file:** `docs/reviews/<yyyy-mm-dd>-spike-sqlite-esquemas.md` (the date is the day the report is written)
- **Throwaway code:** a console or test project under `artifacts/spike-b0-sqlite/` (the `artifacts/` folder is git-ignored; it is not added to `DairyControl.slnx`).
- **Why:** from its first day a module's `DbContext` maps its tables to its own schema (`ToTable("X", "produccion")`, ADR 0004) and the tests of Infrastructure run on in-memory SQLite (CLAUDE.md, Testing), which has no schemas. Today no test passes a schema.

Experiments (each one is a row of the report with the observed result, the SQLite provider version and the exact error text when it fails):

| # | Experiment | What to record |
|---|---|---|
| E1 | One context with two entities, `A` in schema `s1` and `B` in schema `s2`, different table names, `Database.EnsureCreated()` on `DataSource=:memory:` | Does it create both tables? With which names (qualified or not)? |
| E2 | Two contexts (`C1` maps `s1.HistorialCambios`, `C2` maps `s2.HistorialCambios`, same table name) sharing **one** open SQLite connection, `EnsureCreated()` on each | Does the second call fail (table exists), skip, or create a second table? |
| E3 | Same as E2 after `ATTACH DATABASE ':memory:' AS s1` and `... AS s2` on the shared connection | Does EF generate `"s1"."HistorialCambios"` and does it work? |
| E4 | Insert and read one row from each table of E2/E3 with `AsNoTracking()`, a `Where` and an `OrderByDescending` | Does the LINQ translation use the schema? Do the two tables stay separate? |
| E5 | `ExecuteDeleteAsync()` on the entity of E3 and E1 (CLAUDE.md: `DemoDataSeeder` is the only code that deletes rows) | Does it work with a schema? |
| E6 | A foreign key between `A` (`s1`) and a table of the same schema, and one between two schemas (the legacy exception that lasts until B9) | Does `EnsureCreated()` accept both? |
| E7 | Option "strip the schema under SQLite": a model customizer or convention that sets the schema to `null` when the provider is SQLite | Does it work for E1 and E2? What breaks (E2 collides)? |

### Decisión (required section of the report)

The report must end with these four items, each one a plain sentence, so that B1b can be built without a new question:

1. **Strategy chosen** for the Infrastructure tests of a module with a schema: one of "ATTACH DATABASE per schema on one shared connection", "one SQLite database per context", "strip the schema", or another one the spike proves.
2. **A fixture** (about 20 lines of C#, pasted in the report) that creates the connection and the context(s) with that strategy, and the exact names (type, method) it would have in `tests/DairyControl.Infrastructure.Tests`.
3. **What is not covered** by SQLite (so the CLAUDE.md sentence "SQLite tests don't cover SQL Server-specific behavior" can be extended with it).
4. **Whether `ExecuteDelete` and the cross-schema foreign key (the legacy exception) work** under the chosen strategy.

### Issue B0-2: `Migrate()`, pending model changes and a duplicated `AppDbContext`

- **Title:** `[Spike] Migrate() con contexto vacío y AppDbContext duplicado`
- **Report file:** `docs/reviews/<yyyy-mm-dd>-spike-migrate-y-contexto-duplicado.md`
- **Throwaway code:** under `artifacts/spike-b0-migrate/`, a copy of the current migrations and snapshot is used; the real `src/` is not edited.
- **Database:** SQL Server LocalDB (development only), a database created and dropped by the spike. If LocalDB is not available, the issue stops with `andon`.

| # | Experiment | What to record |
|---|---|---|
| M1 | The real `AppDbContext` model against the current snapshot, `Database.Migrate()` on an empty LocalDB database | Control run: it must succeed. EF Core version. |
| M2 | Add one property to an entity of the copy (a pending model change) and call `Migrate()` | Is `PendingModelChangesWarning` thrown as an error? Exact exception type and message. |
| M3 | Same as M2 with `ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` on the context options | Does `Migrate()` succeed? Is the migrations history table written? |
| M4 | `context.Database.HasPendingModelChanges()` on the unchanged model (M1) and on the changed one (M2), with a connection string that points to a server that does not exist | Does it need to open a connection? Return value in both cases. (B1 builds its guard test on this.) |
| M5 | A context type with an **empty** model (no entities) that uses the snapshot of `AppDbContext` through `MigrationsAssembly` and `Migrate()` | Does it throw? Which message? Does the snapshot/model mismatch behave like M2? |
| M6 | Two projects that both define `DairyControl.Infrastructure.Persistence.AppDbContext`, and a third project that references both (the old migrations refer to the type through the project-level `<Using>` of the csproj) | The compiler error or warning ids (for example CS0436 or CS0104) and whether `TreatWarningsAsErrors` turns them into an error. |
| M7 | Same as M6 when only one of the two projects is referenced by the project that holds the migrations | Does it compile? (This is the shape ADR 0005 chooses for `Legado`.) |

### Decisión (required section of the report)

1. **Is `PendingModelChangesWarning` an error in `Migrate()` on this EF Core version?** Yes or no, with the exception type.
2. **The exact code** that silences it for a frozen context (one line, pasted).
3. **Does `HasPendingModelChanges()` need a database connection?** Yes or no. If yes, B1's guard test (issue B1-2) cannot run without SQL Server and the report says what it needs instead.
4. **Does a duplicated `AppDbContext` compile** in the shapes of M6 and M7, and with which ids.

## Files

- **Touches:** `docs/reviews/<date>-spike-sqlite-esquemas.md`, `docs/reviews/<date>-spike-migrate-y-contexto-duplicado.md`.
- **Does not touch:** anything under `src/`, `tests/`, `client/`, `e2e/`, `infra/`, `DairyControl.slnx`, `CLAUDE.md`, migrations.
- **Migrations:** no.

## Behavior that does not change

All of it: the spike changes no product behavior and no test.

## Tests to write

None. The deliverable is the report. A reviewer checks that every experiment row has an observed result and that the `Decisión` section answers every numbered item with a sentence.

## Rule changes

`none` (B1b and B5 may change rules later using these reports).

## Verification

- `git diff main...HEAD --stat` lists only the report files.
- `dotnet build DairyControl.slnx` still ends with zero warnings (nothing under `src/` or `tests/` changed).

## Open decisions

None in this spec. If an experiment cannot be run (for example LocalDB is missing), the builder stops with `andon` and does not guess the result.
