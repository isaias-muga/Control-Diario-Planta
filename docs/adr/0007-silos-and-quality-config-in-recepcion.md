# ADR 0007: Silos and quality limits stay in Recepción; Producción asks through a reserved contract

- **Status:** proposed
- **Date:** 2026-10-21
- **Decided by:** Isaías Muga (tech lead), pending approval at control 2
- **Module(s):** Recepción, Producción

## Context

The single `ConfiguracionCalidad` row holds the quality limits (acidez, temperatura, agua) and `CantidadSilos`. The analysis of the code found:

- It is read and written only by Recepción code: `ConfiguracionCalidadAppService` (read and write), `ProveedorAppService.RegistrarRecepcionAsync` and `RecepcionAppService.CorregirAsync` (silo validation), `RecepcionQueries` (limits for out-of-range flags). `DemoDataSeeder` resets it.
- `ConfiguracionCalidad.SilosMaximo` is `RecepcionLeche.SiloMax`: the configuration depends on the recepción type, and `RecepcionLeche` has its own 1 to 20 silo check.
- `RecepcionLeche.Silo` is a plain integer with no table behind it. A silo has no data of its own today (no name, capacity or product).
- The first planned link of Producción is "la leche de cada silo" (`reloj-suizo.md` §11): Producción needs to know which silos exist and how much milk each received, not to edit them.

## Decision

`ConfiguracionCalidad` (limits and silo count) belongs to Recepción, with its table name unchanged (renaming is a separate decision with no value today). Producción gets the silos and the liters per silo through a **reserved** contract `IRecepcionSilosQueries` (draft 0.1.0 in `docs/contratos/recepcion.md`), approved when Producción's design confirms its shape. It is not built or consumed before that.

This does not change before or after the data move (ADR 0005): the contract is the same, only Recepción's implementation changes tables.

**Trigger to revisit.** If Producción needs to *edit* silos or to attach data to them (name, capacity, product, state), the silo count leaves `ConfiguracionCalidad` and a module **Planta** owns silos; Recepción and Producción then consume it by contract. That is a new ADR and a migration of one column; until then the extra module would only be a function call with extra steps.

## Alternatives considered

| Option | Why not |
|---|---|
| Plant configuration in the Kernel | The Kernel has no tables (ADR 0001); and only Recepción uses the row |
| A Planta module now | One consumer, no data of its own; speculative |
| Producción reads `ConfiguracionesCalidad` | Reads another module's table, forbidden by the company rule |
| Split the row now (limits in Recepción, silo count in Planta) | A data change with no current benefit and a second concurrency version |

## Consequences

- Good: no move, no new module; Producción has a clear, small contract to review.
- Bad / costs: the name `ConfiguracionCalidad` keeps saying "quality" while holding the silo count (already admitted in a code comment). `ConfiguracionCalidad.SilosMaximo = RecepcionLeche.SiloMax` stays as it is: both types are in Recepción after the split, and the number 20 is not copied (a rule is defined once).
- What must be enforced by a machine: Producción never references `DairyControl.Recepcion.Domain/Application/Infrastructure/Api` (ADR 0008); the contract's cases (silo outside the current count, voided recepciones, `LitrosAPagarRegla`) are tests on the publisher side when it is approved.

## Links

`docs/contratos/recepcion.md`, ADR 0001, ADR 0005.
