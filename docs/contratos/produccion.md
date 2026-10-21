# Contract: Producción

<!-- Audience: agents and the tech lead. English. Placeholder: the module does not exist yet. -->

- **Publisher:** Producción (placeholder; no code, no projects, no tables yet).
- **Consumers of what it publishes:** none.
- **Version:** nothing published.
- **Status:** placeholder. This file exists so the module map is complete and so Producción's first design step has the place where its contracts go.

## What Producción publishes

Nothing in version 1. If a later module needs data from Producción, it is a new contract written here, merged before the consumer starts, and listed with its version in `docs/modulos.md`.

## What Producción consumes (planned, not approved)

| Contract | Publisher | State |
|---|---|---|
| Kernel types and persistence helpers | Kernel | approved with the module map (1.0.0), see `kernel.md` |
| `IUsuarioActual` | Kernel | part of the Kernel |
| `IDirectorioDeIdentidad` | Identidad | approved 1.0.0, see `identidad.md`; only if Producción shows who did what |
| `IRecepcionSilosQueries` | Recepción | **reserved, not approved**, see `recepcion.md`; only if Producción's first milestone consumes the milk of each silo |

## Decision needed before the first screen: roles

`RolUsuario` is a closed enum in the Kernel, and the client `Rol` type, `roleGuard`, the user dialog, the JWT claims and the start page mirror it. Producción must choose, before its first screen, one of two paths:
1. Use the existing roles (`Admin`, `Oficina`, `Recepcion`, `Consulta`) and the six general policies, or
2. Add a role (for example a floor role). That is a **chained Kernel + Identidad + client change** done in its own issues before the module's screens, with the new rows of the authorization matrix.

The choice is made in Producción's stage 3 (screens by role), not by a builder.

## Ground rules for the module (from the first day, no exceptions)

- Own projects under `src/Produccion/` (`Domain`, `Application`, `Infrastructure`, `Api`) and, when it publishes something, `Contracts`; own tests under `tests/Produccion/`.
- Own schema `produccion`, own `ProduccionDbContext` registered with `AddModuleDbContext<ProduccionDbContext>(connectionString, "produccion")`, own migrations with `__EFMigrationsHistory` in that schema. It never maps, reads or joins a table of another schema.
- References other modules' records **by id only**, with no foreign key; asks Identidad for names through `IDirectorioDeIdentidad`.
- Own history table `produccion.HistorialCambios` through `HistoryMapping.MapHistory("produccion")`.
- Registered by its own `AddProduccion()` and switched with `Modules:Produccion:Enabled` (default `false` until the module is ready); merged to `main` every day behind that switch. The CI variant with the module on runs its API tests, its rows of the authorization matrix and its e2e; `compose.yaml` and the production YAML do not name it until it is switched on.
- Its Domain exceptions derive from a Kernel category. Its Domain references nothing but the BCL and `DairyControl.Kernel`.
