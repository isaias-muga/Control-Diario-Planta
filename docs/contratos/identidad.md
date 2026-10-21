# Contract: Identidad

<!-- Audience: agents and the tech lead. English. Source of truth for what Identidad publishes. Approval of the versions lives in docs/modulos.md. -->

- **Publisher:** Identidad (users, operarios, login, sessions, PIN).
- **Consumers:** Recepción (both contracts). Producción may consume `IDirectorioDeIdentidad` (not yet; consuming it needs no contract change).
- **Project:** `DairyControl.Identidad.Contracts` (interfaces, DTOs, nothing else; references only `DairyControl.Kernel`).
- **Status:** proposed. Version 1.0.0 of both contracts. The code is produced by milestone B2 (a `[contract]` issue).
- **Fase A / fase B:** the implementation lives in the transitional infrastructure project and reads the `dbo` tables (`Usuarios`, `Operarios`). In fase B it moves to the Identidad infrastructure project and reads `identidad.*`. **The contract does not change.**

Other modules reference users and operarios **only by id** and ask for anything else through these contracts. They never read `Usuarios` or `Operarios`, and there is no foreign key from another module's table to them.

## `IVerificacionDeOperario` 1.0.0

Replaces the direct call of `VerificacionPinService.ResolverAsync` from `ProveedorAppService.RegistrarRecepcionAsync`.

```csharp
namespace DairyControl.Identidad.Contracts;

[ContractVersion("1.0.0")]
public interface IVerificacionDeOperario
{
    /// <summary>
    /// Identidad decides whether the caller needs a PIN (today: only the Recepcion role, the shared reception phone). When a PIN is
    /// needed it must belong to an active operario, which is returned. When it is not needed the result is null and the PIN is ignored.
    /// The caller is the current request user, read from the ambient IUsuarioActual: outside an HTTP request there is no caller and the
    /// call fails with "No se pudo identificar al usuario.".
    /// </summary>
    /// <exception cref="ReglaDeNegocioException">field "pin": "No se pudo identificar al usuario.", "Ingresá tu PIN de operario.", "PIN incorrecto." or the format error of Operario.ValidarPin.</exception>
    /// <exception cref="DemasiadosIntentosException">"Demasiados intentos con el PIN. Esperá unos minutos o avisá en la oficina."</exception>
    Task<OperarioIdentificadoDto?> ResolverAsync(string? pin);
}

public class OperarioIdentificadoDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";   // never null; the active operario's name
}
```

Because the method does not reveal the role, a future module whose own kiosk also identifies operarios (for example Producción) can use it unchanged: Identidad changes its internal policy, not the contract.

Another error under the same type: when the retries of the attempt counter are exhausted because of concurrent saves, Identidad throws `DemasiadosIntentosException("Demasiados intentos al mismo tiempo. Probá de nuevo.")` (text as it is in `UsuarioRepository`; the milestone copies it exactly). Consumers map the type, not the text.

Rules that stay in Identidad and are not part of the contract: five wrong PINs from the same user within 10 minutes block PIN checks for 5 minutes; the attempt is counted before it is checked; a right PIN gives back only its own attempt; a malformed PIN does not count; the counters live on the `Usuario` row.

Concrete cases (each becomes a test on both sides):

| Caller role | `pin` | Result |
|---|---|---|
| Oficina | `null` | `null` (no PIN needed) |
| Admin | `"1234"` | `null` (the PIN is ignored) |
| Recepcion | `null` or `"   "` | `ReglaDeNegocioException`, field `pin`, "Ingresá tu PIN de operario." |
| Recepcion | `"12"` | `ReglaDeNegocioException`, field `pin`, format error; the attempt is not counted |
| Recepcion | PIN of an inactive operario | `ReglaDeNegocioException`, field `pin`, "PIN incorrecto."; the attempt is counted |
| Recepcion | right PIN of Marta | `OperarioIdentificadoDto { Id = <Marta>, Nombre = "Marta" }` |
| Recepcion | fifth wrong PIN within 10 minutes | `DemasiadosIntentosException` |
| Recepcion | any PIN while blocked | `DemasiadosIntentosException`, nothing is checked |
| Recepcion without an identifiable user (`IUsuarioActual.Id` null or unknown) | any | `ReglaDeNegocioException`, field `pin`, "No se pudo identificar al usuario." |

Transaction note: the attempt counter is saved by Identidad in its own save, before and independent of the recepción that Recepción saves afterwards. This is how it works today; the contract does not make the two atomic.

## `IDirectorioDeIdentidad` 1.0.0

Replaces: `RecepcionQueries.QuienCargoAsync` and `NombresDeUsuariosAsync` (direct reads of `Usuarios` and `Operarios`), the author names read by `HistorialQueries`, and the "first admin" lookup of `DemoDataSeeder` (which today picks the first user with role Admin ordered by username; it does **not** use `AdminUser:Username`).

```csharp
namespace DairyControl.Identidad.Contracts;

[ContractVersion("1.0.0")]
public interface IDirectorioDeIdentidad
{
    /// <summary>Display names of users by id. Ids that do not exist are absent from the result; an empty input gives an empty result.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNombresDeUsuariosAsync(IReadOnlyCollection<Guid> ids);

    /// <summary>Display names of operarios by id. Same rules. Inactive operarios are included: history keeps showing who did what.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNombresDeOperariosAsync(IReadOnlyCollection<Guid> ids);

    /// <summary>
    /// The id of the first user whose role is Admin (active or not), ordered by username (same selection as the demo seeder uses today),
    /// or null when there is none. Used only to credit demo data.
    /// </summary>
    Task<Guid?> GetIdDelPrimerAdministradorAsync();
}
```

Rules:
- Users and operarios are never deleted, so an id written in an audit column always resolves. A missing id is simply absent from the dictionary; the caller decides what to show ("—" in lists); the contract never throws for a missing id.
- Inactive users are included, like inactive operarios.
- At most 500 ids per call; the caller batches (a page of the list is at most 100 rows) and **merges the ids of the same kind into one call** (today `RecepcionQueries` reads users twice, for "registrado por" and "anulada por"; the implementation of the consumer uses one `GetNombresDeUsuariosAsync` call for both).
- The directory returns names only: never username, role, hashes, security stamp, PIN data or the protected flag.

Concrete cases:

| Call | Result |
|---|---|
| `GetNombresDeUsuariosAsync([])` | empty dictionary, no query |
| `GetNombresDeUsuariosAsync([idAna, idInexistente])` | `{ idAna: "Ana" }` |
| `GetNombresDeUsuariosAsync([idLuisInactivo])` | `{ idLuisInactivo: "Luis" }` |
| `GetNombresDeOperariosAsync([idMartaInactiva])` | `{ idMartaInactiva: "Marta" }` |
| `GetIdDelPrimerAdministradorAsync()` with admins `zoe` and `ana` | the id of `ana` |
| `GetIdDelPrimerAdministradorAsync()` with no Admin | `null` |

## Tests

- Publisher (`Identidad` contract tests, in-memory SQLite until fase B): each row of the tables above against the real implementation; the directory never returns a deactivated user or operario as missing; the DTO classes have no member besides the documented ones (a reflection test, so a hash can never be added by accident).
- Consumer (Recepción): every use goes through a Moq double of the interface: registering with role Recepcion calls `ResolverAsync` exactly once and stores `OperarioId`; a `DemasiadosIntentosException` from the double ends as 429 and nothing is saved; the list shows "—" when a name is missing; the list makes at most one call per kind per page (users: one call for both audit columns).
- Architecture: no project of another module references `DairyControl.Identidad.Domain`, `.Application`, `.Infrastructure` or `.Api`; the contract project holds interfaces, DTOs and the attribute only.

## Change history

| Version | Change | Compatible |
|---|---|---|
| 1.0.0 | New contract (extracted from the current code) | — |
