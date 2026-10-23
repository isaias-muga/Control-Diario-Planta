# Technical spec: B1b `DairyControl.Kernel.Persistence`

<!-- Audience: agents and tests. English. Technical milestone: there is no screen, so this spec replaces the screen spec. Same structure for B0, B1, B1b, B5 and B6. -->

- **Module:** Kernel (B1b-1, B1b-2) and Legado (B1b-3)
- **Plan row:** `docs/plan-migracion-modular.md`, section 2, milestone **B1b**; section 4 ("`AddModuleDbContext` y su `produccion.HistorialCambios`"); section 7 (row "`Kernel.Persistence`")
- **ADRs it implements:** ADR 0004 (one schema and one `DbContext` per module, registered with `AddModuleDbContext<T>`), ADR 0006 (history table per module, no foreign key)
- **Contract document:** `docs/contratos/kernel.md`, section "Persistence helpers", version 1.0.0 (the same approved contract as B1-1; this milestone builds its second half, so it is not a new contract and not a change)
- **Blocked by:** B0-1 and B0-2 (their reports), and B1-7 (the whole Kernel adoption chain). B1b-2 is blocked by B1b-1 and B1b-3 by B1b-2 (they share `DairyControl.slnx`, the `Dockerfile` and the architecture tests).
- **Unblocks:** B5 and, through B6, the first Producción issue.

## Goal

Give any module with its own `DbContext` the shared pieces from its first day, and let the monolith use the same pieces, without changing the model of `AppDbContext` by one column.

## Issues of this milestone

| Id | Title | Blocked by |
|---|---|---|
| B1b-1 | `[Kernel] Kernel.Persistence: AddModuleDbContext y MapHistory` | B0-1, B0-2, B1-7 |
| B1b-2 | `[Kernel] Kernel.Persistence: HistorialQueriesBase y UnicidadSql` | B1b-1 |
| B1b-3 | `[Legado] Usar Kernel.Persistence en AppDbContext e Infrastructure` | B1b-2 |

## Issue B1b-1: `AddModuleDbContext` and `MapHistory`

New project `src/Kernel/DairyControl.Kernel.Persistence/DairyControl.Kernel.Persistence.csproj` (name `DairyControl.Kernel.Persistence`), added to `DairyControl.slnx`. References: project `DairyControl.Kernel`, packages `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer` and `Microsoft.Data.SqlClient` at the versions that `src/DairyControl.Infrastructure/DairyControl.Infrastructure.csproj` uses today. Nothing else.

```csharp
namespace DairyControl.Kernel.Persistence;

public static class ModuleDbContextExtensions
{
    // Registers TContext on SQL Server with EnableRetryOnFailure and __EFMigrationsHistory in the given schema.
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string connectionString, string schema)
        where TContext : DbContext;
}

public static class HistoryMapping
{
    // Maps CambioRegistrado to [schema].[HistorialCambios]. No foreign key to any table, including users.
    public static void MapHistory(this ModelBuilder modelBuilder, string schema);
}
```

Behavior of `AddModuleDbContext`: `services.AddDbContext<TContext>(options => options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 10, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null).MigrationsHistoryTable("__EFMigrationsHistory", schema)))`. The retry numbers are the ones `src/DairyControl.Infrastructure/DependencyInjection.cs` uses today. A `null` argument throws `ArgumentNullException`; an empty or whitespace `schema` or `connectionString` throws `InvalidOperationException` (CLAUDE.md forbids `ArgumentException` for rules; this is a programming error, not a business rule). Returns the same `IServiceCollection`.

`MapHistory` configures `CambioRegistrado` exactly as `src/DairyControl.Infrastructure/Persistence/Configurations/CambioRegistradoConfiguration.cs` does today (read it when building), except the foreign key to `Usuario`, which is not mapped:

| Property | Mapping |
|---|---|
| table | `HistorialCambios` in `schema` |
| `Id` | key, `ValueGeneratedNever()` |
| `Entidad` | required, max length `CambioRegistrado.EntidadMaxLength` (20) |
| `EntidadId`, `CorreccionId`, `UsuarioId`, `EnUtc` | as today (plain columns) |
| `Campo` | required, max length `CambioRegistrado.CampoMaxLength` (50) |
| `ValorAnterior`, `ValorNuevo` | max length `CambioRegistrado.ValorMaxLength` (500) |
| `Motivo` | required, max length `MotivoDeCambio.MaxLength` (500) |
| index | `(Entidad, EntidadId, EnUtc)`, not unique |
| foreign keys | none |

### Tests (`tests/Kernel/DairyControl.Kernel.Tests`, project reference added)

| # | Case | Input | Expected |
|---|---|---|---|
| P1 | History table | a context with `modelBuilder.MapHistory("produccion")`, model read without a connection (SQL Server provider, never opened) | entity type `CambioRegistrado`: table `HistorialCambios`, schema `produccion` |
| P2 | Columns | the same model | the ten columns above with the lengths of the table; `Entidad` 20, `Campo` 50, `ValorAnterior` and `ValorNuevo` 500, `Motivo` 500 |
| P3 | Index | the same model | one index on `Entidad`, `EntidadId`, `EnUtc` in that order |
| P4 | No foreign key | the same model | `GetForeignKeys()` is empty; `UsuarioId` is a `Guid` column |
| P5 | Two schemas | `MapHistory("identidad")` and `MapHistory("recepcion")` in two contexts | each context reports its own schema |
| P6 | History table of migrations | `AddModuleDbContext<FakeContext>(cs, "produccion")`, resolve the context, `GetService<IHistoryRepository>().GetCreateScript()` | the script creates `[produccion].[__EFMigrationsHistory]` |
| P7 | Retry | the same context | `Database.CreateExecutionStrategy().RetriesOnFailure` is `true` |
| P8 | Bad arguments | `null` services, `""` schema, `"  "` connection string | `ArgumentNullException` / `InvalidOperationException` / `InvalidOperationException` |
| P9 | Behavior on a real engine | the fixture and the strategy recorded in the `Decisión` section of the B0-1 report: `EnsureCreated`, insert one `CambioRegistrado`, read it back with `AsNoTracking()` | one row, same values, under that strategy |
| P10 | Architecture | `typeof(HistoryMapping).Assembly.GetReferencedAssemblies()` | only `DairyControl.Kernel`, `Microsoft.EntityFrameworkCore*`, `Microsoft.Data.SqlClient` and BCL assemblies |

### Files

- **Touches:** `DairyControl.slnx`, the new project, `tests/Kernel/DairyControl.Kernel.Tests` (project reference, new test files), `src/DairyControl.Api/Dockerfile` (the `COPY` of the new csproj in the restore block, because B1b-3 makes Infrastructure reference it).
- **Does not touch:** `src/DairyControl.Infrastructure`, `src/DairyControl.Domain`, migrations, `CLAUDE.md`, the monolith's tests.
- **Migrations:** no.
- **Rule changes:** `none` (the rules change in B1b-3).

## Issue B1b-2: `HistorialQueriesBase` and `UnicidadSql`

```csharp
namespace DairyControl.Kernel.Persistence;

// Reads the history of one entity (newest first, one item per correction) from a DbSet of CambioRegistrado.
// A module implements its own IHistorialQueries on top of it and adds the existence check of its entities. Returns the shared CorreccionDto.
public abstract class HistorialQueriesBase { /* GetCorrecciones(DbSet<CambioRegistrado>, string entidad, Guid id): exact signature in "Open decisions" */ }

public static class UnicidadSql { public static bool EsViolacion(DbUpdateException ex); }   // error 2601 / 2627, unchanged
```

- **`HistorialQueriesBase`** reproduces the grouping of `src/DairyControl.Infrastructure/Persistence/Queries/HistorialQueries.cs`: the entries of the entity ordered by `EnUtc` descending, one `CorreccionDto` per `CorreccionId` (taking `CorreccionId`, `EnUtc`, `Motivo` from the first entry), whose `Cambios` hold one `CampoCambiadoDto` per entry. It does not read any table of users: how a module puts the name in `CorreccionDto.Usuario` is in "Open decisions".
- **`UnicidadSql`** moves from `src/DairyControl.Infrastructure/Persistence/Repositories/UnicidadSql.cs` with `EsNumeroDeUnicidad(int)` and `EsViolacion(DbUpdateException)` (both public, as today). The old file is deleted in B1b-3, which switches its users.
- **Tests** (`tests/Kernel/DairyControl.Kernel.Tests`):

| # | Case | Input | Expected |
|---|---|---|---|
| H1 | One correction, three fields | three entries with the same `CorreccionId` for one entity | one `CorreccionDto` with three `Cambios` |
| H2 | Order | two corrections at 10:00 and 11:00 UTC | the 11:00 one first |
| H3 | Other entities | entries of another `EntidadId` and of another `Entidad` with the same id | not returned |
| H4 | Nothing recorded | an entity without entries | an empty list |
| H5 | Real engine | cases H1 to H3 under the strategy of the `Decisión` section of the B0-1 report, with a non-default schema | same results |
| U1 | Unique violation | `DbUpdateException` whose inner `SqlException.Number` is 2601, then 2627 | `EsViolacion` is `true` for both |
| U2 | Other error | number 547, and an inner exception that is not a `SqlException`, and `null` | `false` |
| U3 | Numbers | `EsNumeroDeUnicidad(2601)`, `(2627)`, `(1)` | `true`, `true`, `false` |

  The U1 to U3 cases are the ones in `tests/DairyControl.Infrastructure.Tests/UnicidadSqlTests.cs`; that file is deleted in B1b-3 after they are copied here (this issue copies them, B1b-3 removes the old file in the same PR that removes the old class).
- **Files touched:** the new classes and tests. **Not touched:** `src/DairyControl.Infrastructure` (B1b-3).
- **Rule changes:** `none`.

## Issue B1b-3: the monolith uses `Kernel.Persistence`

- `DairyControl.Infrastructure` gets `ProjectReference`s to `DairyControl.Kernel` and `DairyControl.Kernel.Persistence`.
- `AppDbContext.OnModelCreating` calls `modelBuilder.MapHistory("dbo")` and **keeps the foreign key** `CambioRegistrado.UsuarioId -> Usuario` (`HasOne<Usuario>().WithMany().HasForeignKey(c => c.UsuarioId).OnDelete(DeleteBehavior.Restrict)`), because it is one of the five cross-module foreign keys that stay until milestone B9 (ADR 0005); the configuration class `CambioRegistradoConfiguration.cs` is deleted.
- `HistorialQueries` derives from or uses `HistorialQueriesBase` for the grouping, and keeps reading `Usuarios` for the author's name (a listed exception until B4).
- Every use of `UnicidadSql` points to `DairyControl.Kernel.Persistence.UnicidadSql`; `src/DairyControl.Infrastructure/Persistence/Repositories/UnicidadSql.cs` and `tests/DairyControl.Infrastructure.Tests/UnicidadSqlTests.cs` are deleted.
- `AddInfrastructure` keeps `AddDbContext<AppDbContext>` as it is. **`AppDbContext` is not registered with `AddModuleDbContext` in fase A**: that would change where the legacy chain keeps its `__EFMigrationsHistory` and is not needed for any other work.
- **The guard of B1-2 decides.** `MapHistory("dbo")` must leave `HasPendingModelChanges()` false and the existing migrations untouched. If it does not (for example because the snapshot has no explicit schema), the builder stops with `andon` and does not edit a migration or the snapshot.
- **Tests:** the whole suite, the B1-2 guard, `HistorialQueries` cases through `CorreccionesYAnulacionTests` (Infrastructure and Api), and in `DependenciasTests`: Infrastructure may reference `DairyControl.Kernel` and `DairyControl.Kernel.Persistence`; Domain, Application and Api must not reference `DairyControl.Kernel.Persistence`; `Kernel.Persistence` references only the allowed assemblies (P10).
- **Files touched:** `DairyControl.Infrastructure.csproj`, `AppDbContext.cs`, `CambioRegistradoConfiguration.cs` (deleted), `HistorialQueries.cs`, `UnicidadSql.cs` (deleted), the repositories that call it, `UnicidadSqlTests.cs` (deleted), `DependenciasTests.cs`. **Not touched:** migrations and the snapshot, the other configurations, `Program.cs`.
- **Rule changes** (`CLAUDE.md`), exact text:
  - "Dependency rules", the Infrastructure bullet becomes: `Infrastructure references Application, Domain, `DairyControl.Kernel` and `DairyControl.Kernel.Persistence`.` and add the bullet `Only Infrastructure references `DairyControl.Kernel.Persistence`.`
  - "Infrastructure conventions": add the bullet `A `DbContext` of a module is registered with `AddModuleDbContext<T>(connectionString, schema)` and maps its history with `MapHistory(schema)` (both from `DairyControl.Kernel.Persistence`); the legacy `AppDbContext` keeps `AddDbContext` and maps its history with `MapHistory("dbo")` plus the foreign key to `Usuario`, which is a listed exception until milestone B9. Unique-violation detection is `UnicidadSql` of `DairyControl.Kernel.Persistence`.`

## Verification (every issue)

- `dotnet build DairyControl.slnx`: zero warnings.
- `dotnet test --solution DairyControl.slnx`: all green, including the B1-2 guard and the architecture tests.
- B1b-1 and B1b-3: `docker compose build` (the `Dockerfile` restore step finds every project).

## Behavior that does not change

The model of `AppDbContext` (checked by the guard), every query result, the history rows, the error numbers treated as unique violations.

## Open decisions (gaps in the approved documents)

1. **B1b-2:** `kernel.md` gives `HistorialQueriesBase { /* GetCorrecciones(DbSet<CambioRegistrado>, string entidad, Guid id) */ }` with no return type, no `static`/instance, no sync or async, and no accessibility. The exact signature is missing.
2. **B1b-2:** `CorreccionDto` has `Usuario` (a name) and `CambioRegistrado` only the `UsuarioId`; the document does not say how the base fills `Usuario` (a callback, a dictionary parameter, or the module fills it afterwards).
3. **B1b-1:** the document says `EnableRetryOnFailure` without numbers; this spec uses the ones of `AddInfrastructure` (10 retries, 30 s). Confirm.
4. **B1b-1:** the document does not say what a bad `schema` or `connectionString` throws; this spec says `ArgumentNullException` for `null` and `InvalidOperationException` for empty. Confirm.
5. **B1b-1, B1b-2:** the strategy for tests on SQLite with schemas comes from the `Decisión` section of the B0-1 report, which does not exist yet.
