# Contract: Recepción

<!-- Audience: agents and the tech lead. English. Source of truth for what Recepción publishes. -->

- **Publisher:** Recepción (proveedores, recepciones de leche, calidad, silos, tickets).
- **Consumers:** none approved yet. Producción is the expected consumer of the reserved contract below.
- **Project:** `DairyControl.Recepcion.Contracts` (created when the reserved contract is approved; references only `DairyControl.Kernel`).
- **Status:** **no approved contract.** Version 1.0.0 of Recepción publishes nothing. The section below is a **draft: do not build it, do not consume it, do not give an issue a `listo` label for it.** It is not in the "Quién usa a quién" table of `docs/modulos.md`, so the approval rule keeps it out of the flow until the tech lead moves it there.

## Reserved: `IRecepcionSilosQueries` (draft 0.1.0, NOT approved)

Why it exists: the first connection of Producción to Recepción is "la leche de cada silo" (`reloj-suizo.md` §11). The draft is written now so the shape is discussed once, but Producción's screens are not designed yet; the real shape is confirmed when Producción's stage 2 and 3 end, and then it is approved with its table row.

```csharp
namespace DairyControl.Recepcion.Contracts;

[ContractVersion("0.1.0")]   // draft; becomes 1.0.0 on approval
public interface IRecepcionSilosQueries
{
    /// <summary>The silo numbers of the plant today: 1..CantidadSilos.</summary>
    Task<IReadOnlyList<int>> GetSilosAsync();

    /// <summary>
    /// Liters received per silo for recepciones whose FechaHora (plant time) is in [desdePlanta, hastaPlanta), only valid recepciones
    /// (RecepcionLeche.Vigente: voided ones are left out). Recepciones without a silo (rows from before the silo was required) are left out.
    /// Silos with no recepción in the period are not returned.
    /// </summary>
    /// <exception cref="ReglaDeNegocioException">field "hasta" when hastaPlanta is not after desdePlanta, or the range is longer than 366 days.</exception>
    Task<IReadOnlyList<LitrosPorSiloDto>> GetLitrosRecibidosPorSiloAsync(DateTime desdePlanta, DateTime hastaPlanta);
}

public class LitrosPorSiloDto
{
    public int Silo { get; set; }
    public decimal LitrosReales { get; set; }     // sum of Litros as received (3 decimals)
    public decimal LitrosAPagar { get; set; }     // sum of LitrosAPagarRegla, computed in SQL, rounded to 3 decimals after the query
    public int Recepciones { get; set; }
}
```

Rules the implementation must keep:
- "Litros a pagar" is computed with `LitrosAPagarRegla` inside the query; the formula is never copied into the contract or into Producción.
- "Valid" is `RecepcionLeche.Vigente`; nobody copies `AnuladaEn == null`.
- Dates are plant time (`HoraPlanta`); the caller never converts.
- Pull, not push: Producción asks when it needs the data. Events (`RecepcionRegistrada`, `RecepcionAnulada`, `RecepcionCorregida`) are deferred on purpose (ADR 0002) because a void or a correction changes the past, and a query always answers with the current truth. If Producción needs to react, the event is a new contract with its own issue.

**Same before and after fase B.** In fase A the implementation lives in the transitional infrastructure project and reads `dbo.Recepciones` and `dbo.ConfiguracionesCalidad`. In fase B it moves to the Recepción infrastructure project and reads `recepcion.Recepciones` and `recepcion.ConfiguracionesCalidad`. Only the implementation changes; the signature, the DTO, the rules and the contract tests do not. Producción can consume it from its first day.

Open questions for Producción's design (answered before approval):
1. Does Producción need the current stock of a silo (liters in minus liters out)? Out is Producción's own data, so it does not need Recepción to compute it.
2. Does it need the proveedor (traceability by tambo)? If yes, a second query returning proveedor ids and names per silo and period is a new method (minor version), not a table read.
3. Does it need to edit the silo count or add per-silo data (capacity, product)? If yes, the silo count leaves `ConfiguracionCalidad` and a "Planta" module is created (ADR 0007).

Concrete cases to turn into tests on approval:

| Setup (silo count 3) | Call | Result |
|---|---|---|
| A: silo 1, 1000 L, water 2 %; B: silo 1, 500 L, water null; C: silo 2, 200 L voided | period covering all | silo 1: LitrosReales 1500, LitrosAPagar 1480, Recepciones 2; silo 2 absent |
| D: silo null (old row) | period covering it | absent |
| none | `GetSilosAsync` | `[1, 2, 3]` |
| the plant lowers the count to 2 and an old recepción has silo 3 | `GetSilosAsync` | `[1, 2]`; the old recepción still counts in `GetLitrosRecibidosPorSiloAsync` as silo 3 (lowering the count never touches history) |

## Change history

| Version | Change | Compatible |
|---|---|---|
| 0.1.0 | Draft, not approved | — |
