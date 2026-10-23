# Technical spec: B1 Kernel

<!-- Audience: agents and tests. English. Technical milestone: there is no screen, so this spec replaces the screen spec. Same structure for B0, B1, B1b, B5 and B6. -->

- **Module:** Kernel (the contract issue) and Legado (the adoption issues: the current monolith `DairyControl.Domain/Application/Infrastructure/Api`, treated as one pseudo-module as in B5)
- **Plan row:** `docs/plan-migracion-modular.md`, section 2, milestone **B1**; section 4; section 7 (rows "Host que referencia entrañas de Identidad", "`EF Core Migrate()` falla con cambios pendientes del modelo")
- **ADRs it implements:** ADR 0002 (contracts reference only the Kernel), ADR 0003 (project layout), ADR 0006 (history per module: the Kernel holds only the type)
- **Contract document:** `docs/contratos/kernel.md`, version 1.0.0, approved in `docs/modulos.md` (section 5, first row)
- **Blocked by:** nothing for B1-1 (parallel with B0). B1-2 to B1-7 are a chain (the Kernel changes in a chain, and they share `DairyControl.slnx`, the `Dockerfile` and the architecture tests).
- **Unblocks:** B1b.

## Goal

Create the `DairyControl.Kernel` project as the single home of the shared types, then move the monolith onto it one concept at a time. **Rule of the chain:** each adoption issue deletes, in the same PR, the old type it replaces, so two active versions of one concept never coexist for longer than one issue. The only exception is the Kernel project itself, which B1-1 creates before anything uses it.

No behavior changes: same HTTP statuses and bodies, same messages, same tables and columns, same JWT contents.

## Issues of this milestone

| Id | Title | Blocked by |
|---|---|---|
| B1-1 | `[contract] Kernel (tipos compartidos y ayudantes de persistencia)` | none |
| B1-2 | `[Legado] Prueba guarda HasPendingModelChanges del AppDbContext` | B1-1 |
| B1-3 | `[Legado] Adoptar RolUsuario, IUsuarioActual, HoraPlanta, MotivoDeCambio y Politicas del Kernel` | B1-2 |
| B1-4 | `[Legado] Adoptar categorías de excepción y middleware por categoría` | B1-3 |
| B1-5 | `[Legado] Adoptar CambioRegistrado del Kernel (sin lista de entidades) y partir Campos` | B1-4 |
| B1-6 | `[Legado] Adoptar DTOs comunes, ApiErrors y ApiConventions` | B1-5 |
| B1-7 | `[Legado] Kernel.TestSupport y TimeProvider.System en el host` | B1-6 |

## Issue B1-1: `[contract] Kernel`

Creates three new projects and their tests. **Touches no existing project** (the monolith keeps its own copies until B1-3 to B1-7 remove them one by one).

### Projects

| Path | Project | References |
|---|---|---|
| `src/Kernel/DairyControl.Kernel/DairyControl.Kernel.csproj` | `DairyControl.Kernel` | BCL only (no package, no project) |
| `src/Kernel/DairyControl.Kernel.TestSupport/DairyControl.Kernel.TestSupport.csproj` | `DairyControl.Kernel.TestSupport` | `DairyControl.Kernel` |
| `tests/Kernel/DairyControl.Kernel.Tests/DairyControl.Kernel.Tests.csproj` | `DairyControl.Kernel.Tests` (xUnit v3, like the other test projects) | `DairyControl.Kernel`, `DairyControl.Kernel.TestSupport` |

All three are added to `DairyControl.slnx`. `Directory.Build.props` applies (nullable, warnings as errors). `DairyControl.Kernel.Persistence` is **not** created here (B1b).

### Signatures (the code is copied from the current file; names in `docs/contratos/kernel.md` win where it differs, see "Decisions taken")

```csharp
namespace DairyControl.Kernel;

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public sealed class ContractVersionAttribute(string version) : Attribute { public string Version { get; } = version; }

public enum RolUsuario { Admin, Oficina, Recepcion, Consulta }          // copy of Domain/Enums/RolUsuario.cs, same order

public interface IUsuarioActual { Guid? Id { get; } RolUsuario? Rol { get; } }

public static class HoraPlanta { /* copy of Application/HoraPlanta.cs: ZonaHoraria, Ahora(TimeProvider), AUtc(DateTime), ALaPlanta(DateTime) */ }

public record MotivoDeCambio { /* copy of Domain/ValueObjects/MotivoDeCambio.cs: MinLength 5, MaxLength 500, Texto, static Create(string?) */ }
```

```csharp
namespace DairyControl.Kernel.Exceptions;

public abstract class ExcepcionDeDominio : Exception { protected ExcepcionDeDominio(string mensaje) : base(mensaje) { } }
public sealed class ReglaDeNegocioException(string mensaje, string? campo = null) : ExcepcionDeDominio(mensaje) { public string? Campo { get; } }
public abstract class ConflictoException(string mensaje) : ExcepcionDeDominio(mensaje);
public sealed class ConflictoDeConcurrenciaException : ConflictoException { /* same two constructors and MensajePredeterminado as today */ }
public sealed class ConflictoDeUnicidadException : ConflictoException { /* same constructors as today */ }
public abstract class AccesoDenegadoDeDominioException(string mensaje) : ExcepcionDeDominio(mensaje);
public sealed class DemasiadosIntentosException : ExcepcionDeDominio { public DemasiadosIntentosException(string mensaje) : base(mensaje) { } }
```

```csharp
namespace DairyControl.Kernel.Auditing;

public class CambioRegistrado
{
    public const int EntidadMaxLength = 20;
    public const int CampoMaxLength = 50;
    public const int ValorMaxLength = 500;
    // properties as today (Id, Entidad, EntidadId, CorreccionId, Campo, ValorAnterior, ValorNuevo, UsuarioId, EnUtc, Motivo), private set,
    // private parameterless constructor. The factory is `public static CambioRegistrado Registrar(string entidad, Guid entidadId, Guid correccionId,
    // string campo, string? valorAnterior, string? valorNuevo, Guid usuarioId, DateTime enUtc, MotivoDeCambio motivo)`: the name and the
    // parameters of today's `Crear`, renamed as `docs/contratos/kernel.md` says.
    // Validation of Entidad: not null/whitespace and at most EntidadMaxLength characters, field "entidad" (no list of names).
}
```

```csharp
namespace DairyControl.Kernel.Contracts;

public class MotivoCambioDto { public string Motivo { get; set; } = ""; }   // as in kernel.md (today `string? Motivo`; the domain answers the same 400 `motivo` for null and for "")
public class CampoCambiadoDto { public string Campo { get; set; } = null!; public string? ValorAnterior { get; set; } public string? ValorNuevo { get; set; } }
public class CorreccionDto { public Guid CorreccionId { get; set; } public DateTime EnUtc { get; set; } public string? Usuario { get; set; } public string Motivo { get; set; } = null!; public List<CampoCambiadoDto> Cambios { get; set; } = []; }
public static class ApiErrors { public static object Body(string error, string? field = null); }   // same result as Api/Validation/ApiErrors.Body: { error } or { error, field }
public static class ApiConventions
{
    public const string LoginRateLimitPolicy = "login";
    public const string MustChangePasswordCode = "debeCambiarPassword";
    public static class Claims { public const string Sub = "sub"; public const string Rol = "role"; public const string Sello = "stamp"; public const string Nombre = "name"; }   // values of JwtRegisteredClaimNames.Sub and AuthService.ClaimRol/ClaimSello/ClaimNombre; the `dcp` claim stays in AuthService (kernel.md does not list it)
}
```

```csharp
namespace DairyControl.Kernel.Authorization;
public static class Politicas { /* the six const string of Api/Authorization/Politicas.cs, same names and values: Lectura, LecturaCarga, Registro, Administracion, Autenticado, CambioPassword */ }
```

`DairyControl.Kernel.TestSupport`: `RelojFijo` (copy of `tests/DairyControl.Application.Tests/RelojFijo.cs`, namespace `DairyControl.Kernel.TestSupport`) and `UsuarioActualFijo` (`public sealed class UsuarioActualFijo(Guid? id, RolUsuario? rol) : IUsuarioActual`, returning the two values it was built with).

### Contract tests

Publisher side, in `tests/Kernel/DairyControl.Kernel.Tests` (cases as `input -> expected`):

| # | Case | Input | Expected |
|---|---|---|---|
| K1 | Entidad accepted at the limit | `Entidad = new string('a', 20)` | entry created |
| K2 | Entidad over the limit | 21 characters | `ReglaDeNegocioException`, `Campo == "entidad"` |
| K3 | Entidad empty or whitespace | `""`, `"  "` | `ReglaDeNegocioException`, `Campo == "entidad"` |
| K4 | Entidad is not a list | `"Produccion"` (never named in the Kernel) | entry created |
| K5 | Append-only | reflection over `CambioRegistrado` | no public setter on any property |
| K6 | Motivo | `MotivoDeCambio.Create("  abc  ")` / `Create("abcde")` / 501 characters | exception field `motivo` / created, `Texto == "abcde"` / exception field `motivo` |
| K7 | Exception categories | a fake class per category in the test project | `ConflictoDeConcurrenciaException` and `ConflictoDeUnicidadException` are `ConflictoException`; a fake derived from `AccesoDenegadoDeDominioException` is an `ExcepcionDeDominio`; `ReglaDeNegocioException("m","c").Campo == "c"` |
| K8 | Kernel references nothing | `typeof(ContractVersionAttribute).Assembly.GetReferencedAssemblies()` | only `System.*`, `netstandard` and `mscorlib` names |
| K9 | HoraPlanta | `RelojFijo(2026-10-23T03:00:00Z)` -> `Ahora`; `AUtc(2026-10-23 00:00 Unspecified)`; `ALaPlanta(2026-10-23 03:00 Utc)`; `ALaPlanta(2026-10-23 00:00 Unspecified)` | `2026-10-23 00:00`; `2026-10-23 03:00 Utc`; `2026-10-23 00:00`; the same value |
| K10 | Policies | the six constants | values equal to the strings above |
| K11 | ApiErrors.Body | `Body("x")`, `Body("x","y")` serialized with `System.Text.Json` | `{"error":"x"}`, `{"error":"x","field":"y"}` |
| K12 | Claims and conventions | the constants | `sub`, `role`, `stamp`, `name`, `login`, `debeCambiarPassword` |

Consumer side (there is no consumer in the code yet; each adoption issue is the consumer): K13, a test that a class receiving `IUsuarioActual` works with `UsuarioActualFijo`, and K14, `RelojFijo.GetUtcNow()` returns always the same instant.

### Files

- **Touches:** `DairyControl.slnx`, the three new projects above.
- **Does not touch:** `src/DairyControl.*`, `tests/DairyControl.*`, `Dockerfile`, `compose.yaml`, migrations, `CLAUDE.md`, `client/`.
- **Migrations:** no.

### Rule changes

`none` (the rules change in the adoption issues).

## Issue B1-2: `[Legado] Prueba guarda HasPendingModelChanges del AppDbContext`

The first adoption issue, **before any type is moved**. Why: since EF Core 9 `Migrate()` throws on pending model changes, and production applies migrations at startup (`docs/production.md`). Any drift between the model and the snapshot caused by the next moves would stop production at boot.

- **Test:** new file `tests/DairyControl.Infrastructure.Tests/ModeloContraSnapshotTests.cs`, class `ModeloContraSnapshotTests`.
  - `AppDbContext_ModeloActual_NoTieneCambiosPendientes`: builds `AppDbContext` with `UseSqlServer("Server=(localdb)\\guarda;Database=guarda;")` (it never connects) and asserts `context.Database.HasPendingModelChanges() == false`.
  - `AppDbContext_ModeloConCambio_ReportaCambiosPendientes` (the guard must be able to fail): the same check on a context whose model differs from the snapshot (for example a subclass or a model customizer that adds one property) returns `true`. How the difference is produced is up to the builder, as long as no file under `src/` changes.
- If `HasPendingModelChanges()` needs a live connection (B0-2, experiment M4), the builder stops with `andon`; this spec does not choose another technique.
- **Files:** touches `tests/DairyControl.Infrastructure.Tests/ModeloContraSnapshotTests.cs` and, if the test project does not reach the SQL Server provider, its `.csproj` (it already references Infrastructure, which references the provider). Does not touch `src/`, migrations, or other test files.
- **Migrations:** no.
- **Rule change** in `CLAUDE.md`, section "Testing", bullet "Only `DairyControl.Infrastructure.Tests` may use a database, and only an in-memory SQLite one ...": append the sentence `One test builds the model of AppDbContext with the SQL Server provider without opening a connection (ModeloContraSnapshotTests): it requires HasPendingModelChanges() to be false and stays green until the legacy AppDbContext is retired (milestone B10).`
- **Stays green until B10:** every later issue of the chain re-runs it.

## Issue B1-3: adopt `RolUsuario`, `IUsuarioActual`, `HoraPlanta`, `MotivoDeCambio` and `Politicas`

For each of the five types, in the same PR: every `using` and reference of the monolith points to the Kernel, and the old definition and its old tests are **deleted**.

| Type | Old file (deleted) | Kernel type |
|---|---|---|
| `RolUsuario` | `src/DairyControl.Domain/Enums/RolUsuario.cs` | `DairyControl.Kernel.RolUsuario` |
| `IUsuarioActual` | the interface at the end of `src/DairyControl.Application/Seguridad/Interfaces.cs` (the two hasher interfaces stay) | `DairyControl.Kernel.IUsuarioActual` |
| `HoraPlanta` | `src/DairyControl.Application/HoraPlanta.cs` | `DairyControl.Kernel.HoraPlanta` |
| `MotivoDeCambio` | `src/DairyControl.Domain/ValueObjects/MotivoDeCambio.cs`, and `tests/DairyControl.Domain.Tests/MotivoDeCambioTests.cs` (already covered by K6 of B1-1; move any case K6 lacks) | `DairyControl.Kernel.MotivoDeCambio` |
| `Politicas` | `src/DairyControl.Api/Authorization/Politicas.cs` | `DairyControl.Kernel.Authorization.Politicas` |

- `ProjectReference` to `DairyControl.Kernel` added to Domain, Application, Infrastructure, Api and the test projects that need it. `src/DairyControl.Api/Dockerfile` gets `COPY src/Kernel/DairyControl.Kernel/DairyControl.Kernel.csproj src/Kernel/DairyControl.Kernel/` in the restore block.
- `tests/DairyControl.ArchitectureTests/DependenciasTests.cs`: Domain may reference the BCL and `DairyControl.Kernel` only; Application may reference `DairyControl.Domain` and `DairyControl.Kernel` only; Infrastructure and Api keep their rules plus the Kernel. The same file gets one new case: the `Kernel` project references nothing outside the BCL.
- Behavior that must not change: the database column of `Usuario.Rol` (a string conversion by name), the JWT claim `role`, every policy name and value, `HoraPlanta` results.
- **Tests:** the existing suites pass unchanged except for `using` lines; `DependenciasTests` cases above; the B1-2 guard stays green.
- **Files touched:** the `*.csproj` of the five projects, `Dockerfile`, every `.cs` that names one of the five types (the compiler lists them), `DependenciasTests.cs`. **Not touched:** migrations, `client/`, `e2e/`, `compose.yaml`, the Kernel project (it only changes by contract change).
- **Rule changes** (`CLAUDE.md`), exact text:
  - "Dependency rules", first two bullets become: `Domain references no other project and no external packages, except `DairyControl.Kernel`.` and `Application references only Domain and `DairyControl.Kernel`.`
  - "Project structure": add the bullet `` `src/Kernel/`: `DairyControl.Kernel` (shared types, BCL only) and `DairyControl.Kernel.TestSupport`; `tests/Kernel/DairyControl.Kernel.Tests` ``.
  - "Domain conventions", bullet "Every exception ...": unchanged here (B1-4).

## Issue B1-4: adopt the exception categories and the middleware

- Delete from `src/DairyControl.Domain/Exceptions/`: `ExcepcionDeDominio.cs`, `ReglaDeNegocioException.cs`, `ConflictoDeConcurrenciaException.cs`, `ConflictoDeUnicidadException.cs`, `DemasiadosIntentosException.cs`. Every reference uses `DairyControl.Kernel.Exceptions`.
- Keep in Domain, now deriving from a category: `TicketDuplicadoException : ConflictoException` and `CuentaProtegidaException : AccesoDenegadoDeDominioException` (constructors and messages as today).
- `ExceptionHandlingMiddleware` maps by category: `ReglaDeNegocioException` 400 `{ error, field? }`, `ConflictoException` 409, `AccesoDenegadoDeDominioException` 403, `DemasiadosIntentosException` 429, request aborted by the client logged at Debug, everything else a logged 500 with the trace id. The messages and the bodies do not change.
- **Tests** (`ExceptionHandlingMiddlewareTests`, `DominioTests`):

| # | Case | Input | Expected |
|---|---|---|---|
| E1 | Rule | `throw new ReglaDeNegocioException("m", "campo")` | 400, `{"error":"m","field":"campo"}` |
| E2 | Rule without field | `new ReglaDeNegocioException("m")` | 400, `{"error":"m"}` |
| E3 | Conflict categories | `TicketDuplicadoException`, `ConflictoDeConcurrenciaException`, `ConflictoDeUnicidadException` | 409 each, body `{"error":<message>}` |
| E4 | A conflict the middleware has never seen | a fake class derived from `ConflictoException` in the test | 409 |
| E5 | Access denied category | `CuentaProtegidaException`, and a fake derived from `AccesoDenegadoDeDominioException` | 403 each |
| E6 | Too many attempts | `DemasiadosIntentosException("m")` | 429 |
| E7 | Anything else | `ArgumentException`, `ArgumentNullException` | 500 `{"error":"Ocurrió un error inesperado.","traceId":...}`, logged at Error |
| E8 | Architecture | every exception type in `DairyControl.Domain.Exceptions` | derives from `ConflictoException` or `AccesoDenegadoDeDominioException` (the sealed categories are used directly) |

- **Files touched:** the five deleted files, the two kept exceptions, the middleware, `ExceptionHandlingMiddlewareTests.cs`, `DominioTests.cs` and every file that names a moved exception. **Not touched:** controllers' routes and policies, `client/`, migrations.
- **Rule changes** (`CLAUDE.md`), exact text:
  - "Domain conventions", bullet "Every exception the domain raises on purpose ...": replace the first sentence with `Every exception the domain raises on purpose derives from `ExcepcionDeDominio` (in `DairyControl.Kernel.Exceptions`) through one of its categories: `ReglaDeNegocioException` (400), a `ConflictoException` (409), an `AccesoDenegadoDeDominioException` (403) or `DemasiadosIntentosException` (429).` and the bullet "The other domain exceptions ..." becomes `The other domain exceptions are `TicketDuplicadoException` (a `ConflictoException`: ticket already used) and `ConflictoDeConcurrenciaException` (a `ConflictoException`: stale edit of the quality limits).`
  - "API conventions", bullet `ExceptionHandlingMiddleware` maps ...: replace the type list with `ExceptionHandlingMiddleware` maps by category: `ReglaDeNegocioException` to 400 `{ "error": "...", "field": "..." }` (`field` only when there is a `campo`), every `ConflictoException` to 409 `{ "error": "..." }`, `DemasiadosIntentosException` to 429, every `AccesoDenegadoDeDominioException` to 403, and every other exception ... (the rest of the sentence as today).`
  - "Never do": add the bullet `An exception type that the exception middleware has to know by name: a new exception derives from a Kernel category.`

## Issue B1-5: adopt `CambioRegistrado` from the Kernel and split `Campos`

- Delete `src/DairyControl.Domain/Entities/CambioRegistrado.cs` and `tests/DairyControl.Domain.Tests/CambioRegistradoTests.cs` (covered by K1 to K5 of B1-1; move any missing case). Domain, Application and Infrastructure use `DairyControl.Kernel.Auditing.CambioRegistrado`, and every call to `CambioRegistrado.Crear(...)` becomes `CambioRegistrado.Registrar(...)` (same arguments).
- The entity names and field names become constants of two new classes in the monolith's Domain (location in "Decisions taken"): `RecepcionCampos` (`EntidadRecepcion = "Recepcion"`, `EntidadProveedor = "Proveedor"`, and the fields `proveedor, litros, grasa, acidez, temperatura, agua, silo, ticket, fechaHora, observaciones, anulacion, nombre, activo`) and `IdentidadCampos` (`EntidadUsuario = "Usuario"`, `EntidadOperario = "Operario"`, and the fields `nombre, activo, alta, rol, password, pin`). Every value is exactly the string used today, so no history row changes.
- `CambioRegistradoConfiguration` keeps the table and columns; `Entidad` uses `CambioRegistrado.EntidadMaxLength` (20, today the literal `HasMaxLength(20)`). The B1-2 guard must stay green.
- **Tests:** every existing correction and history test passes unchanged; new Domain test `RecepcionCampos_Constantes_SonLasDeHoy` and `IdentidadCampos_Constantes_SonLasDeHoy` asserting the strings above; an Infrastructure test that a `CambioRegistrado` with `Entidad = "Produccion"` is saved and read back (no list of names in the Kernel).
- **Files touched:** the deleted entity and test, the two new constant classes, `CambioRegistradoConfiguration.cs`, every file that names `CambioRegistrado.Campos` or `CambioRegistrado.Entidad*` (entities, `RecepcionAppService`, `ProveedorCambiosAppService`, `UsuarioAppService`, `OperarioAppService`, `HistorialQueries`, `DemoDataSeeder`, tests). **Not touched:** migrations, the history table, DTOs.
- **Rule changes** (`CLAUDE.md`), exact text:
  - "Domain conventions", the `CambioRegistrado` sentence inside "Corrections, voids and deactivation": replace `(own aggregate root, table `HistorialCambios`: ...)` by the same text plus ` The type lives in `DairyControl.Kernel.Auditing` and accepts any non-empty `Entidad` of at most `CambioRegistrado.EntidadMaxLength` (20) characters; the entity names and field names are constants of the module that owns the entity (`RecepcionCampos`, `IdentidadCampos`), never a list in the Kernel.`
  - "Never do": add `A list of entity names or field names in the Kernel.`

## Issue B1-6: adopt the shared DTOs, `ApiErrors` and `ApiConventions`

- `MotivoCambioDto`, `CampoCambiadoDto`, `CorreccionDto`: delete them from `src/DairyControl.Application/DTOs/CorreccionesDtos.cs` (the other classes of that file stay) and use `DairyControl.Kernel.Contracts`.
- `ApiErrors.Body` moves to the Kernel; `src/DairyControl.Api/Validation/ApiErrors.cs` keeps everything else (`InvalidModelState`, `UseSpanishBindingMessages`, `BodyInesperado`, the two message constants) and loses `Body`. Every controller and the middleware call `DairyControl.Kernel.Contracts.ApiErrors.Body`.
- `ApiConventions`: `AuthService.ClaimRol/ClaimSello/ClaimNombre`, `ProteccionDeAccesoExtensions.PoliticaLogin` and `ResultadoAutorizacion.CodigoDebeCambiarPassword` are deleted and replaced by `ApiConventions.Claims.Rol`, `.Sello`, `.Nombre` (and `.Sub` where `JwtRegisteredClaimNames.Sub` was used; `AuthService.ClaimDebeCambiarPassword` stays), `ApiConventions.LoginRateLimitPolicy` and `ApiConventions.MustChangePasswordCode` (same values). `AutenticacionExtensions`, `UsuarioActualHttp`, `AuthController` and the client contract (`debeCambiarPassword`) are unchanged in behavior.
- **Tests:** `ApiErrorsTests` pass; `AutorizacionTests` and `SesionesYPinTests` pass unchanged; new case: the 403 body for a user that must change the password is `{"code":"debeCambiarPassword"}`.
- **Files touched:** `CorreccionesDtos.cs`, `ApiErrors.cs`, `AuthService.cs`, `ProteccionDeAccesoExtensions.cs`, `ResultadoAutorizacion.cs`, `AutenticacionExtensions.cs`, `UsuarioActualHttp.cs`, `AuthController.cs`, controllers, the middleware and the tests that name them. **Not touched:** `client/`, the JWT contents.
- **Rule changes** (`CLAUDE.md`), exact text: "API conventions", add `The error body is built only with `ApiErrors.Body` of `DairyControl.Kernel.Contracts`, the history DTOs (`CorreccionDto`, `CampoCambiadoDto`, `MotivoCambioDto`) are the Kernel's, and the claim names, the login rate-limit policy name and the `debeCambiarPassword` code are the constants of `ApiConventions`; a module never defines its own copy.`

## Issue B1-7: `Kernel.TestSupport` and `TimeProvider.System` in the host

- `tests/DairyControl.Application.Tests/RelojFijo.cs` is **deleted**; every test project uses `DairyControl.Kernel.TestSupport.RelojFijo` (project reference added). Any fixed `IUsuarioActual` double written inline in a test is replaced by `UsuarioActualFijo` when it has the same meaning.
- `AddApplication()` stops registering `TimeProvider.System`; `Program.cs` registers `builder.Services.AddSingleton(TimeProvider.System)` once. Test hosts that relied on `AddApplication()` for the clock register a `RelojFijo` or the system one explicitly (`FabricaDeApi`).
- **Tests:** a new case in `tests/DairyControl.Api.Tests` (`HealthEndpointTests` or a new small class): `GET /health/live` works and the service provider resolves `TimeProvider` as `TimeProvider.System`; an Application test that `AddApplication()` alone registers no `TimeProvider`.
- **Files touched:** `RelojFijo.cs` (deleted), `DependencyInjection.cs` (Application), `Program.cs`, `FabricaDeApi.cs`, the test `.csproj` files, every test file that names `RelojFijo`. **Not touched:** `src/Kernel`, migrations.
- **Rule changes** (`CLAUDE.md`): "Application conventions", bullet "Every "now" comes from the injected `TimeProvider`": append `The host registers `TimeProvider.System` once for every module; tests use `RelojFijo` and `UsuarioActualFijo` from `DairyControl.Kernel.TestSupport`.`

## Verification (every issue of the milestone)

- `dotnet build DairyControl.slnx`: zero warnings.
- `dotnet test --solution DairyControl.slnx`: all green, including the guard of B1-2 and the architecture tests.
- B1-3, B1-4, B1-6: also `docker compose build` (the `Dockerfile` restore step must find every project) and, for B1-4 and B1-6, the e2e specs related to errors and login on a fresh stack (`npx playwright test roles cuentas-protegidas` from `e2e/`).

## Behavior that does not change

HTTP statuses and bodies, error messages, table and column names, the history rows already stored, JWT claims and their values, policy names, client behavior.

## Decisions taken (following the contract document, and the tech lead's answers)

Where `docs/contratos/kernel.md` and the current code differ, this spec follows the document, because it is the approved contract:

1. `CambioRegistrado`'s factory is `Registrar(...)` (today `Crear(...)`); B1-5 renames the callers.
2. `MotivoCambioDto.Motivo` is `string = ""` (today `string?`); the observable behavior does not change.
3. `ApiConventions.Claims` has the four names the document lists (`sub`, `role`, `stamp`, `name`); the claim `dcp` (`AuthService.ClaimDebeCambiarPassword`) stays in `AuthService` until Identidad is moved. The constant identifiers `Sub`, `Rol`, `Sello`, `Nombre` are derived from the existing `Claim*` names.
4. `UsuarioActualFijo` has the constructor above (the document only says "fixed `IUsuarioActual`").
5. `HoraPlanta` and `MotivoDeCambio` live in the namespace `DairyControl.Kernel` (the block they are listed under).

6. (Tech lead, confirmed) `CambioRegistrado.Registrar`, `MotivoCambioDto.Motivo` as `string = ""` and `dcp` outside `Claims` follow the contract document as written above; no contract change.
7. (Tech lead) `RecepcionCampos` and `IdentidadCampos` live in `src/DairyControl.Domain/Campos/` (namespace `DairyControl.Domain.Campos`) while the module projects do not exist.
8. (Tech lead) The host class `ApiErrors` (`DairyControl.Api.Validation`) keeps its name next to `DairyControl.Kernel.Contracts.ApiErrors`; the namespaces tell them apart.
9. (Tech lead) The constructors of `ConflictoDeUnicidadException` and `DemasiadosIntentosException` are copied unchanged from the current files.

## Open decisions

None.
