# Segunda pasada independiente sobre la división en módulos (2026-10-21)

_Quién: un agente nuevo, sin el contexto de la conversación que escribió la propuesta, con una sola tarea: romper la división. Solo lectura. Material: `docs/modulos.md`, `docs/contratos/*.md`, `docs/adr/0001` a `0010`, `docs/plan-migracion-modular.md`, las reglas de la empresa y el código real. Antes de corregir, se comprobaron en el código los hallazgos que más cambiaban la propuesta: largo de `Entidad` (columna de 20), selección del primer admin en `DemoDataSeeder`, servicio concreto de PIN inyectado en `ProveedorAppService`, lista de specs en `ci.yml`, superposición de revisiones en `docs/production.md`, login de SQL en `infra/production.ps1`. El resto no se reverificó por separado._

## Hallazgos y cómo se resolvió cada uno

| # | Gravedad | Hallazgo | Resolución |
|---|---|---|---|
| F1 | Bloqueante | B3 (mover Identidad) no compila antes de B4: `ProveedorAppService` inyecta el `VerificacionPinService` concreto y `UsuariosDtos.cs` contiene DTOs de Recepción | B2 se partió; **B2c** hace que Recepción consuma los contratos y saque los dos DTOs antes de B3. B3 depende de B2c |
| F2 | Bloqueante | B2 mezclaba contrato, implementación y un segundo publicador; no pasa la definición de "Listo" de un issue `[contract]` | **B2a** solo el contrato; **B2b** la implementación (Identidad); **B2d** `Recepcion.Contracts` como issue de contrato propio, solo tras aprobación |
| F3 | Bloqueante | `Migrate()` de EF Core falla con cambios pendientes del modelo; el plan no tenía guarda y el "Legado con contexto vacío" chocaba con eso y con el nombre `AppDbContext` | Prueba `HasPendingModelChanges` en el primer commit de B1; el `AppDbContext` completo corre la cadena vieja hasta B9; el proyecto Legado nace en B9 y es el único que define `AppDbContext`; B0 confirma el comportamiento (ADR 0005). **No verificado por mí con código**: es conocimiento de EF Core que B0 debe confirmar |
| F4 | Importante | Faltaban en el Kernel `CorreccionDto`, `CampoCambiadoDto`, `MotivoCambioDto`, `ApiErrors`, nombre de la política de login, claims y código 403; el host referenciaba entrañas de Identidad | Agregados a `kernel.md`; JWT, validación de sesión, `UsuarioActualHttp` y ajustes pasan a `AddIdentidad()` (B3); regla del host en ADR 0008 |
| F5 | Importante | `GetIdDeUsuarioPorUsernameAsync` no reemplazaba al seeder (elige por rol, ordenado por username) y dependía de la collation de SQL Server | Reemplazado por `GetIdDelPrimerAdministradorAsync`; casos de prueba actualizados; ya no hay búsqueda por texto |
| F6 | Importante | El corte de la fase B no está garantizado: la revisión vieja sigue atendiendo durante el deploy | Paso 0 de la sección 6.3 (sacar de servicio la revisión vieja) y mismo cuidado en el rollback; ADR 0005. Texto de permisos suavizado (producción usa el login administrador). `EnsureSchema` en lugar de `CREATE SCHEMA` suelto |
| F7 | Importante | La fila `Baseline` del Puente 2 no estaba especificada | Migración `Baseline` real con `Up` vacío e id fijo, el mismo que inserta la migración 12; índices de las FK borradas se conservan con `HasIndex`; prueba de igualdad por contexto |
| F8 | Importante | `IVerificacionDeOperario` fijaba el rol Recepcion y dependía del `IUsuarioActual` ambiente | Contrato reescrito: Identidad decide quién necesita PIN; dependencia ambiente declarada; segundo mensaje de `DemasiadosIntentosException` documentado |
| F9 | Importante | `Entidad` mide 20 en la columna y el Kernel decía 50 | `EntidadMaxLength = 20` |
| F10 | Importante | Los roles de Producción no estaban diseñados (enum cerrado) | Decisión pendiente documentada en `produccion.md` y en la ruta crítica |
| F11 | Importante | Un módulo apagado no corría nunca en CI; el endpoint de módulos falla con "debe cambiar la contraseña" | Variante de CI con el módulo encendido (B6, ADR 0010); C6 lo lee después del login y tolera el 403 |
| F12 | Importante | "B5 no necesita B3/B4" valía a medias: `Proyecto.cs` fija el ensamblado de API y los controladores de módulos quedarían fuera | B5 carga todos los `*.Api`, referencia los proyectos con un patrón y cuenta controladores (ADR 0008, punto 7) |
| F13 | Importante | `CLAUDE.md`, `AutorizacionTests`, `FabricaDeApi` y la lista de specs de CI los editan todos los módulos | Reglas de `CLAUDE.md` por hito; filas de matriz por módulo y punto de extensión en `FabricaDeApi`; E1 actualiza la lista de CI |
| F14 | Importante | Apoyo de tests compartido (`RelojFijo`, usuarios de prueba) sin lugar; tests de Recepción crean `Usuario` de Identidad | `Kernel.TestSupport`; excepción listada hasta B9; las reglas cubren `tests/` |
| F15 | Importante | Cliente: `AuthService.inicio` fija rutas de Recepción, API pública incompleta, etiquetas por campo, `tsconfig` sin `projects/ui`, spec con `node:fs` | Inicio por rol registrado por módulo (C3), API pública ampliada (C2), etiquetas por entidad + campo, `tsconfig` (C1), script de Node para la prueba de fronteras (C4). **No verificado**: el comportamiento del builder de tests de Angular; se prototipa en C1 |
| F16 | Menor | Contradicciones: conteo de contratos, fila reservada bajo la línea de aprobación, `SilosMaximo` copiado, nombres técnicos en castellano, comparación con una tabla en prosa, referencias de `Kernel.Persistence`, `TimeProvider`, proyecto transitorio sin nombre | Todo corregido: fila reservada por encima de la línea, `SilosMaximo` se queda como referencia, nombres técnicos en inglés (`ContractVersion`, `AddModuleDbContext`, `MapHistory`, `IModuleStartupStep`), ancla fija para la tabla (ADR 0008), referencias completas, `TimeProvider` en el host, proyecto transitorio nombrado |
| F17 | Menor | La regla del reinicio de demo cambia de sentido tras B9 | ADR 0006, punto 7, y plan sección 7 |
| F18 | Menor | Detalles del directorio: dos lecturas de usuarios, usuarios inactivos, nombres faltantes | Una llamada por tipo, caso de usuario inactivo, nombre faltante = ausente del diccionario |

## Lo que el agente intentó romper y no pudo

- Completitud de acoplamientos en `src/`: lo único que lee `Usuarios` y `Operarios` fuera de Identidad es `RecepcionQueries`, `HistorialQueries`, `DemoDataSeeder` y `VerificacionPinService`. `CreadoPorUsuarioId` nunca se lee, así que no falta un contrato por eso.
- Hay exactamente cinco FK entre módulos en el snapshot; `Recepciones.ProveedorId` es interna a Recepción.
- Atomicidad: ninguna escritura necesita tablas de dos módulos en un `SaveChanges`. El contador de intentos de PIN ya se guarda aparte hoy.
- El mapeo de categorías de excepción coincide con los códigos del middleware actual.
- El `THROW` por conteos dentro del Puente 2 es sólido porque EF envuelve la migración en una transacción.
- Los números de ejemplo de `IRecepcionSilosQueries` son consistentes.
- El stack de e2e y la base nueva funcionan si F3 y F7 se resuelven. La demo se despliega desde otro repositorio, así que la fase B no la toca.

## Sin verificar (a confirmar en B0)

`ALTER SCHEMA ... TRANSFER` con índices filtrados, `RowVersion` y datos sembrados; SQLite con esquemas; comportamiento de `PendingModelChangesWarning` en `Migrate()` con un contexto de modelo vacío.
