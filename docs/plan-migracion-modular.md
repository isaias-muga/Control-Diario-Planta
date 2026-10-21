# Plan de migración a módulos

_Estado: propuesta para el control 2, 21/10/2026. Va junto con `docs/modulos.md`. Cada hito es lo bastante chico para convertirse en uno o varios issues con `/traza:plan-issues` (los hitos de mover código se parten por proyecto). Los IDs (B0, C1, E1...) son los que se usan en los ADR._

## 1. Dos fases y por qué

| | Fase A (ahora) | Fase B (no antes de ~17/11/2026) |
|---|---|---|
| Qué se hace | Estructura nueva: Kernel, contratos, proyectos por módulo, pruebas de fronteras, cliente partido, andamiaje para módulos nuevos | Mover los datos: esquemas, un `DbContext` por módulo, historial partido, FK borradas |
| Tablas existentes | **No se mueven.** Siguen en `dbo`, un solo `AppDbContext` en un proyecto transitorio | Pasan a `identidad.*` y `recepcion.*` |
| Rollback de un deploy | Redeployar el SHA anterior alcanza | **No alcanza**: ver sección 6 |
| Producción (módulo) | Nace en la estructura final: su esquema, su `DbContext`, su historial | Sin cambios |

**Por qué dos fases.** El piloto de recepción tiene una prueba de adopción de 4 semanas (se revisa cerca del 17/11/2026). Durante ese tiempo no se hace cirugía de esquemas sobre producción: lo que ya se usa tiene que quedarse quieto. Todo lo que no mueve datos se puede hacer ya y es lo que desbloquea a Producción.

## 2. Fase A: hitos de backend

Los de backend van **en cadena** entre sí cuando tocan `DairyControl.slnx`, `src/DairyControl.Api/Dockerfile`, `Program.cs` o las pruebas de arquitectura, que son archivos que comparten. Mientras B3 y B4 no terminen, el código de Identidad y Recepción sigue en los proyectos actuales; cuando un hito dice "Identidad" o "Recepción" se refiere a qué parte lógica toca. **El proyecto transitorio es el `DairyControl.Infrastructure` de hoy** (con `AppDbContext`): B3 y B4 lo dejan como el único proyecto que referencia a los dos módulos, y B10 lo retira.

| ID | Hito | Depende de | Qué toca |
|---|---|---|---|
| **B0** | **Spike**, sin cambio de comportamiento, con informe en `docs/reviews/`. (a) SQLite con esquemas: `EnsureCreated`, consultas y `ExecuteDelete`. (b) `Migrate()` con un contexto de modelo vacío y el snapshot actual: confirmar si EF Core lanza `PendingModelChangesWarning` como error (hoy ninguna prueba del repo llama a `HasPendingModelChanges`) y cómo se silencia. (c) Qué pasa si dos proyectos definen `DairyControl.Infrastructure.Persistence.AppDbContext` (las migraciones viejas lo referencian por el `<Using>` del csproj) | — | `tests/`, un proyecto descartable |
| **B1** | **Kernel** (ver `docs/contratos/kernel.md`): tipos, categorías de excepción, `CambioRegistrado` sin lista fija de entidades (`EntidadMaxLength = 20`, el largo actual de la columna), DTOs comunes (`CorreccionDto`, `CampoCambiadoDto`, `MotivoCambioDto`), `ApiErrors`, constantes de política de login y de claims, `Kernel.TestSupport` (`RelojFijo`, `UsuarioActualFijo`), `TimeProvider.System` registrado por el host. El middleware mapea por categoría. **Primer commit del hito, antes de mover nada:** una prueba que arma el modelo real de `AppDbContext` con el proveedor de SQL Server y exige `HasPendingModelChanges() == false`; se mantiene en verde hasta B10 (un desvío del modelo contra el snapshot hace fallar `Migrate()` al arrancar producción) | — (puede ir junto a B0) | Domain, Application, Api, tests |
| **B1b** | `DairyControl.Kernel.Persistence`: `AddModuleDbContext`, `MapHistory`, `HistorialQueriesBase`, `UnicidadSql` | B0, B1 | Kernel.Persistence, Infrastructure |
| **B2a** | `[contract]` **Identidad 1.0.0**: solo el proyecto `Identidad.Contracts` (interfaces, DTOs, `ContractVersion`), dobles de Moq y pruebas de forma. Nada de las entrañas de un módulo | B1 | Contracts |
| **B2b** | **Identidad**: implementar los dos contratos en el proyecto transitorio (leen `dbo`) con sus pruebas del lado del publicador | B2a | Identidad |
| **B2c** | **Recepción** pasa a ser consumidora: `ProveedorAppService` usa `IVerificacionDeOperario`, `RecepcionQueries` usa `IDirectorioDeIdentidad` (una sola llamada de usuarios para "registrado por" y "anulada por"), `ProveedorOpcionDto` y `RecepcionRegistradaDto` salen de `UsuariosDtos.cs`, `DemoDataSeeder` usa `GetIdDelPrimerAdministradorAsync`. Pruebas del lado consumidor con dobles. **Esto va antes de B3**: si B3 mueve `VerificacionPinService` y `Operario` mientras Recepción los usa directo, el código no compila | B2b | Recepción |
| **B2d** | `[contract]` **Recepcion.Contracts** con `IRecepcionSilosQueries`. **Solo después de aprobar el contrato** (ver sección 4) y como su propio issue de contrato; después, su implementación sobre `dbo` en un issue de Recepción | aprobación | Contracts, Recepción |
| **B3** | **Identidad**: mover `Usuario`, `Operario`, `Auth*`, PIN, controladores y tests a `src/Identidad/*`. Entran en `AddIdentidad()`: el registro del JWT, la validación de sesión (`AuthService.ValidarSesionAsync`), `UsuarioActualHttp`, `PasswordVigenteRequirement`, los ajustes `Jwt`/`Pin`/`AdminUser` y sus claves de arranque, y el paso de arranque del primer admin y las cuentas protegidas (`IModuleStartupStep`), para que el host no referencie entrañas de Identidad. Las filas de Usuarios, Operarios y Auth de `AutorizacionTests` pasan a un archivo propio del módulo; `FabricaDeApi` gana un punto de extensión por módulo. Sigue mapeado a `dbo` | B2c, B6 | `src/Identidad`, `tests/Identidad` más archivos de host |
| **B4** | **Recepción**: mover proveedores, recepciones, configuración, servicios, consultas, controladores y tests a `src/Recepcion/*`. `RegistrarRecepcionAsync` pasa a `RecepcionAppService` (misma ruta `POST /api/Proveedores/{id}/recepciones`). `HistorialQueries` se parte en un lector por módulo sobre `dbo.HistorialCambios`. `LeerVersion` y `MensajeTicketDuplicado` a un ayudante interno del módulo. `ConfiguracionCalidad.SilosMaximo` sigue apuntando a `RecepcionLeche.SiloMax` (dentro del módulo, sin copiar el número). Sus filas de `AutorizacionTests` a un archivo propio | B3 | `src/Recepcion`, `tests/Recepcion` más archivos de host |
| **B5** | **Pruebas de arquitectura de fronteras** (ADR 0008) con la lista de excepciones de la fase A. Detalles que no se pueden omitir: el proyecto de pruebas referencia todos los proyectos de módulo con un patrón de MSBuild (`src/**/*.csproj`), para que un módulo nuevo no obligue a editarlo (a verificar); se cargan **todos** los ensamblados `*.Api` y la prueba falla si cuenta menos controladores que los esperados (hoy 5); las reglas cubren `src/` **y** `tests/`; el monolito actual cuenta como un seudo-módulo "Legado" | B1 | `tests/DairyControl.ArchitectureTests` |
| **B6** | **Andamiaje de módulos nuevos**: `Add<Modulo>()`, interruptor `Modules:<Modulo>:Enabled` (apagado por defecto), `GET /api/Modulos`, claves de arranque por módulo en `ConfiguracionDeArranque`, salud por contexto, pasos de arranque en orden (legado, luego módulos), una variante de CI con el módulo nuevo encendido (su API y su e2e corren ahí), `CODEOWNERS` base, la lista "Cómo se agrega un módulo" | B1b, B5 | Host, `compose.yaml`, `.github/workflows/ci.yml` |

**Reglas del `CLAUDE.md` que cambian ("Rule changes")**, para que cada PR las escriba y no se pisen: B1 "Domain conventions" y "Never do"; B2a a B2c "Roles and users" (el PIN y los nombres por contrato) y "Application conventions"; B3 y B4 "Project structure", "Dependency rules" y "API conventions"; B5 "Architecture tests"; B6 "Docker / configuration flags". Cada hito toca solo sus secciones; el PR de cada uno va en cadena con el anterior.

## 3. Fase A: cliente y e2e (en paralelo con todo el backend)

| ID | Hito | Depende de | Reglas del `CLAUDE.md` |
|---|---|---|---|
| **C1** | Carpetas `modulos/identidad`, `modulos/recepcion`, `shell`, librería `@ui` en `client/projects/ui` y alias en `tsconfig` (mover archivos sin cambiar comportamiento). Incluye: que `tsconfig.app.json`, `tsconfig.spec.json` y el `unit-test` de `angular.json` incluyan `projects/ui` (hoy solo `src/**`); `testing/sesion.ts` como API de prueba pública | — | "Client conventions" (carpetas) |
| **C2** | Partir `limits.ts`, `correcciones.ts`, los estilos globales (`.page:has(app-...)`) y `motivo-dialog`; `PIN_LENGTH` y `authInterceptor` por `@identidad/publico`; el registro de etiquetas del historial **con clave entidad + campo** (hoy el mapa es solo por nombre de campo, y `silo`, `litros`, `nombre` pueden chocar) | C1 | "Client conventions" (límites, autenticación) |
| **C3** | Navegación **y página de inicio por rol** registradas por cada módulo (hoy `AuthService.inicio` fija `/inicio`, `/resumen` y `/recepciones`, que son de Recepción); rutas con `loadChildren` | C2 | "Client conventions" (roles y rutas) |
| **C4** | Prueba de fronteras del cliente con su lista de excepciones. Se hace como **script de Node que corre en CI**, no como spec de vitest: el `unit-test` de Angular empaqueta con esbuild y puede no resolver `node:fs` (se prototipa en C1) | C1 (se afina con C2 y C3) | "Client conventions" |
| **C5** | Borrar `components/confirmar-accion-dialog` (nadie lo usa) | — | "Client conventions" (lista de componentes) |
| **C6** | El cliente lee `GET /api/Modulos` y oculta rutas y navegación de módulos apagados. Lo lee **después del login** y trata el 403 `debeCambiarPassword` como "ninguno por ahora", porque el endpoint exige contraseña vigente | C3 y **B6** | "Client conventions" |
| **E1** | Partir `e2e/helpers.ts` y los specs mezclados (`roles.spec.ts`, `navegacion-movil.spec.ts`) por módulo, **y actualizar la lista de specs de la segunda corrida de CI** (`.github/workflows/ci.yml`, hoy `carga-rapida roles correcciones cuentas-protegidas`) para que un spec renombrado no se saltee | C1, B4 | "E2E tests" |
| **E2** | `CODEOWNERS` completo por carpeta y formulario de issue `[contract]` | B4, C1 | "Git workflow" |

C6 es el único hito de cliente que depende de un hito de backend; está marcado. Si dos hitos en paralelo tocan la misma sección del `CLAUDE.md`, el segundo se rebasa sobre el primero.

## 4. Ruta crítica para arrancar Producción

**Antes del primer issue de Producción, en este orden:** `B0` y `B1` (pueden ir juntos) → `B1b` → `B5` → `B6`.

| Hito | Por qué lo necesita Producción |
|---|---|
| B0 | Sus tests de Infrastructure corren sobre SQLite y necesitan esquemas |
| B1 | Los tipos y las categorías de excepción del Kernel que va a usar |
| B1b | `AddModuleDbContext` y su `produccion.HistorialCambios` |
| B5 | Las reglas de fronteras estrictas para un módulo nuevo. B5 **no necesita** B3 ni B4: mientras tanto el monolito cuenta como un seudo-módulo "Legado" en la lista de excepciones |
| B6 | Interruptor, registro, arranque y la lista de pasos para crear un módulo |

**Además, según lo que consuma su primer hito:**
- `B2a` y `B2b` si muestra quién hizo cada cosa (`IDirectorioDeIdentidad`). `B2c` (Recepción como consumidora) no le hace falta.
- `B2d`, el contrato `Recepcion.Contracts` con `IRecepcionSilosQueries`, si consume la leche de cada silo. Hoy ese contrato está **reservado**: para usarlo hay que aprobarlo (versión en la tabla de `docs/modulos.md`, o el comentario `Aprobado` si ya figura como contrato existente) y un issue de Recepción que lo implemente sobre `dbo`. Producción puede diseñar pantallas antes (etapas 2 y 3), así que ahí se confirma.
- Si su primer hito necesita un **rol nuevo** (por ejemplo uno de planta), antes de su primera pantalla hace falta un issue en cadena de Kernel + Identidad (ver `docs/contratos/produccion.md`).
- Si su primer hito no consume Recepción ni Identidad, la ruta crítica es solo `B0, B1 → B1b → B5 → B6`.

**En paralelo con Producción:** `B2a` a `B2c`, `B3`, `B4`, `C1` a `C6`, `E1`, `E2`. Producción solo toca `src/Produccion`; ellos tocan `src/Identidad`, `src/Recepcion`, `client/` y `e2e/`.

**Razón técnica de orden (no de dependencia):** B3 y B4 editan los mismos archivos de host que B6 (`DairyControl.slnx`, `Dockerfile`, `Program.cs`, pruebas de arquitectura). Por eso B6 se mergea primero y B3 y B4 se rebasan sobre él. Ninguno hace falta para que Producción compile, se pruebe o se despliegue.

**Qué se verificó de "B5 no necesita B3 ni B4".** Vale para las reglas de referencias, contratos, esquemas y descubrimiento, que se aplican a las carpetas nuevas de `src/<Modulo>/` con el monolito como seudo-módulo. No vale sin dos cosas que B5 incluye: cargar todos los ensamblados `*.Api` (hoy el ensamblado de la API está fijo en `Proyecto.cs`, y los controladores de un módulo nuevo quedarían fuera sin avisar) y contar controladores.

**Cómo se prueba un módulo apagado.** Un módulo con el interruptor en `false` no corre en el CI normal. B6 agrega una variante de CI con `Modules__<Modulo>__Enabled=true` donde corren su API, sus filas de la matriz de autorización y su e2e; `compose.yaml` y los YAML de producción no nombran el módulo hasta que se encienda en un ambiente.

## 5. Fase B: mudanza de datos

**No empieza antes de la revisión de adopción (~17/11/2026) ni sin los hitos B3 y B4 mergeados.** Todos estos hitos llevan la marca **deploy solo**: un deploy propio, sin otro PR mergeado en el medio, y siguen la plantilla de la sección 6.

| ID | Hito | Depende de | Marca |
|---|---|---|---|
| **B7** | **Puente 1**: crear los esquemas `identidad` y `recepcion` y `ALTER SCHEMA ... TRANSFER` de `Usuarios`, `Operarios`, `Proveedores`, `Recepciones` y `ConfiguracionesCalidad`. `AppDbContext` mapea con esquema en el mismo deploy | B3, B4, adopción revisada | **deploy solo** |
| **B8a** | `IdentidadDbContext` y proyecto `Identidad.Infrastructure` (repositorios, consultas, `IDirectorioDeIdentidad` y `IVerificacionDeOperario` sobre `identidad.*`). Cada contexto de módulo tiene una **prueba de igualdad con su modelo** (el esquema que produce coincide con el de la base migrada) | B7 | normal (el historial sigue en `dbo`, ver B9) |
| **B8b** | `RecepcionDbContext` y proyecto `Recepcion.Infrastructure` (repositorios, consultas, implementación de `IRecepcionSilosQueries` si ya está aprobado, seeder y reinicio de demo). Mismas pruebas | B7 | normal |
| **B9** | **Puente 2**: tablas de historial por módulo, copia por `Entidad`, comparación de conteos, `DROP` de `dbo.HistorialCambios`, **5 FK borradas**, historiales de migraciones por esquema con la fila de la migración `Baseline` de cada módulo (ver abajo); las migraciones viejas, el `AppDbContext` completo y el snapshot se mudan al proyecto Legado | B8a, B8b | **deploy solo** |
| **B10** | Salud por contexto, `Infrastructure.Tests` por módulo (un fixture SQLite por contexto), retiro del proyecto transitorio y de la prueba `HasPendingModelChanges` del `AppDbContext` viejo (que pasa a una por contexto) | B9 | normal |
| **B11** | Lista de excepciones de las pruebas de fronteras en cero, `CLAUDE.md` final y revisión de arquitectura (`docs/architecture-review.md`) | B10, C4, E1 | normal |

B8a y B8b van **en paralelo** (carpetas distintas); su integración con el host va en cadena.

**Quién corre las migraciones 11 y 12 entre B7 y B9.** El `AppDbContext` completo (todos los mapeos, sin sacar nada) **sigue existiendo hasta B9** y es el único que corre la cadena vieja (`Database.Migrate()`), aunque el código de los módulos ya lea y escriba con `IdentidadDbContext` y `RecepcionDbContext`. El costo es que las mismas tablas están mapeadas dos veces durante ese tramo; por eso las pruebas de igualdad de B8a y B8b y la de `HasPendingModelChanges` son obligatorias. Entre B7 y B9 ambos contextos de módulo mapean `dbo.HistorialCambios`, cada uno solo con sus entidades. En B9 la cadena vieja, el `AppDbContext` y el snapshot se mudan **juntos** al proyecto Legado, que es el único que define `AppDbContext` (así no hay dos tipos con el mismo nombre en ensamblados que se referencian), y su contexto se declara con `ConfigureWarnings` para ignorar `PendingModelChangesWarning`, porque queda congelado (a confirmar en B0).

**La migración `Baseline` de cada módulo.** Cada cadena de módulo empieza con una migración real de `Up` vacío con id fijo (`<yyyymmddhhmmss>_Baseline`, el mismo valor en `Identidad` y `Recepcion`, elegido al escribir B8a y B8b). La migración 12 del Puente 2 inserta exactamente ese id en `identidad.__EFMigrationsHistory` y `recepcion.__EFMigrationsHistory`; si no coinciden, `Migrate()` intentaría crear tablas que ya existen. Los esquemas se crean con `EnsureSchema`, no con `CREATE SCHEMA` suelto dentro de `Sql()`. Al borrar las 5 FK quedan sus índices (`IX_Recepciones_RegistradoPorUsuarioId`, `IX_Recepciones_OperarioId`, `IX_Recepciones_AnuladaPorUsuarioId`, `IX_Proveedores_CreadoPorUsuarioId`): el modelo de cada módulo los conserva con `HasIndex`, para que la próxima migración no emita un `DropIndex` inesperado. Una prueba compara una base armada desde la migración 1 hasta la última (cadena vieja y luego cada módulo) con el modelo de cada contexto.

**Qué se hace con las FK actuales (pedido del líder técnico).** En la fase A siguen y figuran como excepciones. En B9, dentro de la misma transacción del Puente 2, se borran las cinco: `Recepciones.RegistradoPorUsuarioId`, `Recepciones.OperarioId`, `Recepciones.AnuladaPorUsuarioId`, `Proveedores.CreadoPorUsuarioId` e `HistorialCambios.UsuarioId`. Las columnas quedan como `Guid` simples con sus datos intactos; los nombres se piden con `IDirectorioDeIdentidad`. La integridad la sostiene la regla "usuarios y operarios no se borran" más un test de contrato (ADR 0006).

## 6. Plantilla de un hito de mudanza de datos (B7 y B9)

Todo lo que cambia la nube lo ejecuta una persona (regla de la empresa); Claude no corre estos comandos. Cada paso que lleva comandos de Azure o de base queda en un script revisado.

**6.1 Ensayo (una semana antes y otra vez si el código cambia)**
1. Restaurar producción a un punto en el tiempo en una **base aparte** (`ControlDiario-ensayo`). Este paso es también el simulacro de restauración pendiente de `docs/production.md`.
2. Correr la migración candidata contra esa copia, con el **mismo login que usa producción** (hoy la cadena de conexión usa el administrador del servidor SQL, según `infra/production.ps1`, así que el riesgo de permisos es bajo; igual se ensaya con ese login, porque `ALTER SCHEMA ... TRANSFER` descarta los permisos otorgados directamente sobre cada tabla).
3. Contar filas por tabla antes y después (incluido el historial separado por `Entidad`), y comparar índices, FK y permisos. Si algo no coincide, el hito no sale.
4. Levantar la API contra la copia y hacer la prueba rápida (ver 6.3).
5. Medir el corte (cuánto tarda la migración) y ensayar el rollback completo una vez.
6. Guardar el resultado en `docs/reviews/<fecha>-ensayo-<hito>.md`.

**6.2 Antes del deploy**
1. Elegir un horario **fuera del horario de carga**, dentro de la ventana diaria.
2. Avisar a la oficina con un día de anticipación: qué día y a qué hora, cuánto dura el corte y que no cargue recepciones en esa franja; y avisar de nuevo al empezar.
3. Anotar el **SHA anterior** (el destino del rollback) y confirmar que existe su imagen en el registro.
4. Confirmar que ningún otro PR se mergea en el medio (**deploy solo**).
5. Confirmar la retención de restauración a un punto en el tiempo de la base y que hay una restauración reciente comprobada (ensayo).

**6.3 Durante el deploy**
0. **Sacar de servicio la revisión vieja antes de empezar.** Hoy el deploy deja atendiendo a la revisión anterior hasta que la nueva esté lista (`docs/production.md`), y esa revisión consulta `dbo.Usuarios` o `dbo.HistorialCambios`: durante el corte daría errores 500 y las escrituras que alcance a recibir se perderían sin avisar. Una persona desactiva la revisión activa de `cdf-api` (script revisado; `az containerapp revision list` en solo lectura confirma que no queda ninguna activa) y, recién entonces, sigue. Esto se ensaya en 6.1.
1. Anotar la hora **T0 en UTC**, exacta, justo antes de iniciar el workflow `deploy-production` (comentario en el issue).
2. Correr el workflow con el SHA del hito. Seguir el log de los pasos de arranque (cada migración es un `PasoDeArranque`).
3. **Prueba rápida (2 minutos):** `GET /health` en 200 por el cliente; login de admin; el listado de recepciones muestra el mismo total que antes; se abre el historial de una recepción, de un proveedor y de un usuario; el celular de recepción llega a su pantalla de inicio. No se carga ninguna recepción de prueba (serían datos reales).
4. Repetir los conteos por tabla en modo lectura y compararlos con los de antes del deploy.

**6.4 Rollback (si falla cualquier paso de 6.3, o los conteos no coinciden)**
Redeployar el SHA anterior **no alcanza**: el código viejo no entiende el esquema nuevo.
1. Avisar a la oficina que no cargue nada. Anotar la hora **T1 en UTC**.
2. Restaurar la base **al punto exacto anterior a T0** en una base nueva (`ControlDiario-restaurada`).
3. Reemplazar: renombrar la base actual a `ControlDiario-fallida-<fecha>` y la restaurada a `ControlDiario` (si el servicio no permite renombrar, cambiar el secreto de la cadena de conexión; esto se ensaya en 6.1).
4. Redeployar el **SHA anterior** con el mismo workflow (`sha`), con la revisión fallida ya fuera de servicio (misma regla que en 6.3, paso 0).
5. Repetir la prueba rápida de 6.3 y los conteos; deben ser los de antes del deploy.
6. Avisar a la oficina. Dejar un comentario con `andon` en el issue con lo que falló. Conservar la base fallida hasta entender la causa.

Lo que se haya cargado entre T0 y el reemplazo se pierde. Por eso el deploy es fuera del horario de carga y la oficina está avisada.

## 7. Dependencias ocultas entre fases

Cualquier hito de la fase A que dependa de un paso de la fase B figura aquí. La revisión de dependencias se hizo hito por hito y la segunda pasada la volvió a chequear.

| Qué | Dónde se nota | Cómo se maneja |
|---|---|---|
| `IRecepcionSilosQueries` | Producción lo consume desde el primer día | Mismo contrato antes y después de B8b; solo cambia la tabla que lee la implementación |
| Una sola cadena de migraciones para Identidad y Recepción | B3, B4 y cualquier issue de esos módulos que agregue una migración | Esos issues van en cadena entre sí hasta B9 (Producción tiene su cadena y no entra en esto) |
| "Sin FK entre esquemas" y "un historial por módulo" | Reglas de B5 | Valen desde el primer día **solo para módulos nuevos**; para Identidad y Recepción son excepciones listadas hasta B9 |
| `Kernel.Persistence` | Producción en la fase A | Es un hito de la fase A (B1b), no de la B |
| Spike de SQLite con esquemas | Tests de Producción en la fase A | B0 está en la fase A |
| Interruptores `Modules:<Modulo>:Enabled` | Identidad y Recepción no tienen interruptor | Solo importa para módulos nuevos; B6 lo documenta |
| `DemoDataSeeder` | Borra filas y está en el proyecto transitorio | Se queda ahí en la fase A (única excepción que borra, ya existente); en B8b pasa a Recepción y la prueba que lee su ruta se actualiza en el mismo PR. Después de B9 la regla "no toca el historial de usuarios y operarios" se vuelve trivial (esas filas ya no están en la tabla de Recepción); la regla real pasa a ser "el seeder de Recepción nunca nombra el esquema `identidad`", y esa es la que la prueba debe chequear |
| Tests de Recepción que crean usuarios | `UsuariosDePrueba.CrearAdmin` crea un `Usuario` de Identidad porque hoy existen las FK de auditoría | Excepción listada de la fase A: esos tests pueden referenciar `Identidad.Domain` hasta B9. En B9 dejan de necesitarlo y la excepción se borra. `RelojFijo` y `UsuarioActualFijo` viven en `Kernel.TestSupport` desde B1 |
| Archivos que editan todos los módulos | `AutorizacionTests`, `FabricaDeApi`, `CLAUDE.md`, lista de specs de CI | Un archivo de filas por módulo y un punto de extensión en `FabricaDeApi` (B3, B4); cada hito toca solo sus secciones del `CLAUDE.md` (sección 2); la lista de CI se actualiza en E1 |
| Host que referencia entrañas de Identidad (JWT, validación de sesión, `UsuarioActualHttp`, `ConfiguracionDeArranque`) | `AutenticacionExtensions.cs`, `Program.cs` | Entra en `AddIdentidad()` en B3; el host solo referencia `*.Api` de los módulos y el Kernel |
| `EF Core Migrate()` falla con cambios pendientes del modelo | Producción arranca aplicando migraciones | Prueba `HasPendingModelChanges` desde el primer commit de B1; B0 confirma el comportamiento |
| Dos revisiones superpuestas en un deploy | Fase B | Sección 6.3, paso 0 |
| Health | Un `AppDbContext` hoy | En la fase A el chequeo sigue sobre `AppDbContext` y suma uno por cada contexto de módulo nuevo; en B10 queda uno por contexto |
| Rollback | Fase A se revierte con el SHA anterior; fase B no | Sección 6 |
| `GET /api/Modulos` | C6 lo necesita | B6 lo publica; C6 está marcado como dependiente |

## 8. Paralelo y cadena, de un vistazo

| Se puede hacer en paralelo | Va en cadena |
|---|---|
| Producción (apenas B0, B1, B1b, B5 y B6 estén mergeados) con B2a a B2c, B3, B4, C1 a C6, E1 y E2 | B0/B1 → B1b → B5 → B6 → B2a → B2b → B2c → B3 → B4 |
| Cliente (C1 a C5) con todo el backend | C1 → C2 → C3 → C6 |
| B8a con B8b | B7 → (B8a ∥ B8b) → B9 → B10 → B11 |
| B0 con B1 | Cualquier cambio del Kernel o de Identidad (regla de la empresa) |
| | Issues de Identidad y Recepción que agregan migraciones, hasta B9 |
