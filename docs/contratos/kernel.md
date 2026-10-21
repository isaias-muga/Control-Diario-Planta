# Contract: Kernel (shared types)

<!-- Audience: agents and the tech lead. English. Source of truth for the public surface of `DairyControl.Kernel`. Approval of the version lives in docs/modulos.md. -->

- **Publisher:** Kernel (no tables, no behavior beyond the types below).
- **Consumers:** every module (Identidad, Recepción, Producción) and the host.
- **Version:** 1.0.0
- **Status:** proposed (not built yet; the code is produced by milestone B1, see `docs/plan-migracion-modular.md`).
- **Change rule:** a change to this surface is a contract change: andon, the tech lead decides, version bump, all module owners are told. The Kernel changes in a chain, never in parallel with another Kernel change.

The Kernel is not a module with data. It holds the types that every module must share to behave the same way. Anything with a table, a screen or a business rule of one module does not belong here.

## Namespaces and projects

| Project | Allowed references | Contains |
|---|---|---|
| `DairyControl.Kernel` | BCL only | everything below except `Persistencia` |
| `DairyControl.Kernel.Persistence` | `DairyControl.Kernel`, EF Core, the SQL Server provider, `Microsoft.Data.SqlClient` | shared EF mapping and helpers for module `DbContext`s (see "Persistence helpers") |
| `DairyControl.Kernel.TestSupport` | `DairyControl.Kernel` | test helpers used by the test projects of every module: `RelojFijo` (fixed `TimeProvider`) and `UsuarioActualFijo` (fixed `IUsuarioActual`). Referenced only from `tests/` |

## Types (signatures are the target; names that exist today are kept)

```csharp
namespace DairyControl.Kernel;

// Marks a published contract interface. The version is compared with docs/modulos.md by an architecture test.
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public sealed class ContractVersionAttribute(string version) : Attribute
{
    public string Version { get; } = version;
}

// Today: DairyControl.Domain.Enums.RolUsuario
public enum RolUsuario { Admin, Oficina, Recepcion, Consulta }

// Today: DairyControl.Application.Seguridad.IUsuarioActual (implemented by the host from the validated token)
public interface IUsuarioActual
{
    Guid? Id { get; }
    RolUsuario? Rol { get; }
}

// Today: DairyControl.Application.HoraPlanta (static; time zone America/Argentina/Buenos_Aires). Signatures are copied unchanged by the milestone.
// Today: DairyControl.Domain.ValueObjects.MotivoDeCambio (trimmed, 5 to 500 characters, field "motivo"). Unchanged.
```

### Exceptions

Today every exception is `sealed` and the middleware maps each type to a status. A module cannot add an exception the host does not know. The Kernel therefore defines **category bases** and the middleware maps categories only.

```csharp
namespace DairyControl.Kernel.Exceptions;

public abstract class ExcepcionDeDominio : Exception { /* unchanged */ }

// 400 { error, field? }
public sealed class ReglaDeNegocioException(string mensaje, string? campo = null) : ExcepcionDeDominio(mensaje) { public string? Campo { get; } }

// 409 { error }
public abstract class ConflictoException(string mensaje) : ExcepcionDeDominio(mensaje);
public sealed class ConflictoDeConcurrenciaException : ConflictoException { /* unchanged: stale edit */ }
public sealed class ConflictoDeUnicidadException : ConflictoException { /* unchanged: username or PIN already taken */ }

// 403 { error }
public abstract class AccesoDenegadoDeDominioException(string mensaje) : ExcepcionDeDominio(mensaje);

// 429 { error }
public sealed class DemasiadosIntentosException(string mensaje) : ExcepcionDeDominio(mensaje);
```

Exceptions of one module live in that module and derive from a category base:

| Today | Goes to | Derives from |
|---|---|---|
| `TicketDuplicadoException` | Recepción | `ConflictoException` |
| `CuentaProtegidaException` | Identidad | `AccesoDenegadoDeDominioException` |

`ExceptionHandlingMiddleware` (host) maps: `ReglaDeNegocioException` 400, `ConflictoException` 409, `AccesoDenegadoDeDominioException` 403, `DemasiadosIntentosException` 429, everything else a logged 500. The messages and status codes seen by the client do not change. An architecture test requires every exception in every module's Domain to derive from one of these categories.

### Change history (`CambioRegistrado`)

Today `CambioRegistrado.Entidad` is validated against a fixed list of four entity names, and `CambioRegistrado.Campos` holds the field names of all four entities. Both would force a Kernel change for every new module. Target:

```csharp
namespace DairyControl.Kernel.Auditing;

// Append-only. Same shape as today (Entidad, EntidadId, CorreccionId, Campo, ValorAnterior, ValorNuevo, UsuarioId, EnUtc, Motivo).
// Entidad is any non-empty string of at most CambioRegistrado.EntidadMaxLength (20) characters chosen by the owning module (constants live in the module, e.g.
// RecepcionCampos.EntidadRecepcion). The Kernel no longer lists entity names or field names.
public class CambioRegistrado
{
    public const int EntidadMaxLength = 20;   // the current column length; the history table mapping reuses it
    /* factory Registrar(...) validates non-empty, length and MotivoDeCambio; no setters */
}
```

The `Campos` constants are split: `RecepcionCampos` (Recepción), `IdentidadCampos` (Identidad). A module that writes history owns its table (`<schema>.HistorialCambios`); the Kernel owns only the type and the mapping helper.

### Shared DTOs and API conventions

Every module's controllers return the same history and error shapes, and the client has one history dialog. These types are shared so that no module has to reference another to compile, and none invents its own copy:

```csharp
namespace DairyControl.Kernel.Contracts;   // plain classes with get/set, as the DTO convention says

public class MotivoCambioDto { public string Motivo { get; set; } = ""; }                 // today MotivoCambioDto
public class CampoCambiadoDto { /* today CampoCambiadoDto, unchanged */ }
public class CorreccionDto { /* today CorreccionDto, unchanged: one item per correction, newest first */ }

public static class ApiErrors
{
    // Today DairyControl.Api.Validation.ApiErrors.Body: { "error": "...", "field": "..." }; controllers of any module use it
    public static object Body(string error, string? field = null);
}

public static class ApiConventions
{
    public const string LoginRateLimitPolicy = "login";            // registered by the host (rate limiter), used by Identidad's login action
    public const string MustChangePasswordCode = "debeCambiarPassword";   // body code of the 403 the client reacts to
    public static class Claims { /* names of the JWT claims, copied from AuthService.Claim*: sub, role, stamp, name */ }
}
```

The `Claims` names, the 403 code and the policy name are part of this contract: the client reads the code, and the host and Identidad must agree on the claim names (the host reads the role claim in `UsuarioActualHttp`; after the move `UsuarioActualHttp`, the JWT bearer setup and the session validation (`AuthService.ValidarSesionAsync`) belong to Identidad's `AddIdentidad()`, so the host references no Identidad internals).

`TimeProvider.System` is registered by the host (today `AddApplication()` does it), once, for every module.

### Authorization names

```csharp
namespace DairyControl.Kernel.Authorization;

// Today: DairyControl.Api.Authorization.Politicas. The six general policies keep their meaning:
// Lectura (Admin, Oficina, Consulta; also default and fallback), LecturaCarga (+ Recepcion), Registro (Admin, Oficina, Recepcion),
// Administracion (Admin), Autenticado, CambioPassword.
public static class Politicas { /* six const string, unchanged values */ }
```

A policy that only one module needs is declared by that module (its own constants class) and registered by its `AddXxx()`. The six general ones are defined and registered by the host. **Roles are a closed enum:** a new role (for example a floor role for Producción) is a chained Kernel + Identidad change (enum, user dialog, claims, client `Rol`, `roleGuard`, start page), decided before the module's first screen; see `produccion.md`.

## Persistence helpers (`DairyControl.Kernel.Persistence`)

Needed by any module with its own `DbContext`, including Producción from its first day.

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
    // Maps CambioRegistrado to [schema].[HistorialCambios] with the current columns and the (Entidad, EntidadId, EnUtc) index.
    // No foreign key to any other table, including users: UsuarioId is a plain Guid.
    public static void MapHistory(this ModelBuilder modelBuilder, string schema);
}

// Reads the history of one entity (newest first, one item per correction) from a DbSet of CambioRegistrado.
// A module implements its own IHistorialQueries on top of it and adds the existence check of its entities. Returns the shared CorreccionDto.
public abstract class HistorialQueriesBase { /* GetCorrecciones(DbSet<CambioRegistrado>, string entidad, Guid id) */ }

public static class UnicidadSql { public static bool EsViolacion(DbUpdateException ex); } // unchanged, error 2601 / 2627
```

## What is not in the Kernel

`Politicas` of one module, any repository interface, any query interface, any DTO of a module (except the three shared above), `ConfiguracionCalidad`, `LitrosAPagarRegla`, the quality limits, the silo rule, the PIN rules, the password rules. Those belong to their module.

## Tests

- Publisher side (`tests/Kernel/DairyControl.Kernel.Tests`): `CambioRegistrado` is append-only and validates `Entidad` and `MotivoDeCambio`; every exception category maps to the documented status (with a fake module exception per category); `HistoryMapping` produces the documented columns and index and no foreign key.
- Consumer side: every module has an architecture test that its Domain exceptions derive from a category and that it references no Kernel type outside this document.
- Architecture: the `Kernel` project references nothing but the BCL; `Kernel.Persistence` references only `Kernel`, EF Core, the SQL Server provider and `Microsoft.Data.SqlClient` (for `UnicidadSql`).
