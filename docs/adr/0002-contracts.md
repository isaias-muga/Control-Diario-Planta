# ADR 0002: Contracts are query interfaces in a Contracts project; events are deferred

- **Status:** proposed
- **Date:** 2026-10-21
- **Decided by:** Isaías Muga (tech lead), pending approval at control 2
- **Module(s):** company

## Context

Every edge that crosses a module boundary must become an explicit contract (company rules, "Modules and contracts"). The crossings found are all synchronous questions: "who is this PIN?", "what are the names of these ids?", and, later, "how many liters went into each silo?". None of them needs a module to be told that something happened. Recepciones can be corrected and voided, which changes the past, so a copy kept by a consumer from events would have to be repaired, while a query always answers with the current truth.

## Decision

We publish each contract as a C# interface with its DTO classes in a `DairyControl.<Modulo>.Contracts` project that references only `DairyControl.Kernel`. A contract interface carries `[ContractVersion("MAJOR.MINOR.PATCH")]`. We use queries (pull) first and add an event only when a consumer proves it cannot work with a query, as a new contract with its own issue.

## Alternatives considered

| Option | Why not |
|---|---|
| Events first (outbox, handlers) | Needs infrastructure (outbox table, retries, ordering) that no current edge needs, and copies data that corrections and voids would invalidate |
| Contract interfaces inside `Application` of the publishing module | A consumer would have to reference the whole publisher Application to see an interface, which defeats the boundary test |
| Shared database views | They are reads of another module's tables with extra steps |
| HTTP calls between modules | One process, one deployment: a network hop adds failure modes and latency for nothing |

## Consequences

- Good: a boundary is a project reference that an architecture test can check; consumers test against a Moq double.
- Good: the version in the attribute and the version in `docs/modulos.md` are compared by a test, so a contract cannot change without the table changing.
- Bad / costs: a query contract may cost an extra call per page (the directory is called once per kind per page, at most 100 ids).
- Versioning: MAJOR for a breaking change (andon, the tech lead decides, affected owners are told), MINOR for a compatible addition (a new method or DTO field), PATCH for documentation or behavior that already matched the text. A contract at version 0.x is a draft and cannot be consumed.
- Contract errors: a contract documents the exceptions it throws; they are Kernel exceptions only, so the host maps them without knowing the module.
- What must be enforced by a machine: contract projects contain only interfaces, DTO classes and the attribute; no project references another module's non-contract projects; attribute versions equal the table of `docs/modulos.md`; both sides have contract tests (publisher against the real implementation, consumer against a double).

## Links

`docs/contratos/identidad.md`, `docs/contratos/recepcion.md`, `traza-manual/docs/reloj-suizo.md` ("Los issues de contrato").
