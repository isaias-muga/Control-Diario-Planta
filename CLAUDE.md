# DairyControl

Milk reception and quality-control system for a dairy plant. Backend: .NET 10, ASP.NET Core Web API, EF Core 10, SQL Server (LocalDB in development), xUnit + Moq, Clean Architecture with Domain-Driven Design. Client: Angular (standalone components, signals) in `client/`.

## Project structure

- `DairyControl.slnx` at the repo root
- `src/DairyControl.Domain`: entities, value objects, repository interfaces
- `src/DairyControl.Application`: services, DTOs, settings
- `src/DairyControl.Infrastructure`: `AppDbContext`, EF Core configurations, migrations, repositories
- `src/DairyControl.Api`: controllers, middleware, `Program.cs`
- `tests/DairyControl.Domain.Tests`, `tests/DairyControl.Application.Tests`, `tests/DairyControl.Api.Tests`, `tests/DairyControl.Infrastructure.Tests` and `tests/DairyControl.ArchitectureTests` (the rules of this file that a test can check; see "Architecture tests")
- `client/`: Angular client (see "Client conventions")
- `e2e/`: Playwright end-to-end tests (see "E2E tests")
- `Directory.Build.props`: for every project, `Nullable` enabled, warnings as errors and the latest analysis level. The build must have zero warnings; fix the cause, and never suppress a warning without a justification written next to it. The one exception is the NuGet audit warnings NU1901-NU1904: they stay visible but do not fail the build (see the comment in that file) and are reviewed in the architecture review.
- `docs/`: deployment notes, the architecture review process (`docs/architecture-review.md`) and its records (`docs/reviews/`).
- `global.json`: opts `dotnet test` into Microsoft.Testing.Platform. Required on the .NET 10 SDK; do not delete it or pin an SDK version in it.

## Dependency rules (never break these)

- Domain references no other project and no external packages.
- Application references only Domain.
- Infrastructure references Application and Domain.
- Api references Application and Infrastructure. It is the composition root.
- EF Core types never appear in Domain or Application.
- If a type is needed by both Api and Application, it belongs in Application.
- Services are registered in `AddApplication()` (Application) and `AddInfrastructure(connectionString)` (Infrastructure), called from `Program.cs`; options, authentication, CORS and health checks stay in `Program.cs`.
- These rules are enforced by `tests/DairyControl.ArchitectureTests`: a change that breaks one fails the build.

## Roles and users

- Complexity grows with the role. The reception phone (role `Recepcion`) has one button and a PIN; the office (`Oficina`) reads everything and records the plant lab measurement, that is, registers recepciones with her own user and no PIN, and changes nothing else (no proveedores, no settings, no users); `Admin` also changes data and manages users and operarios. Do not add screens or options to a role that does not need them.
- Each record is created where the fact happens: whoever registers a recepción or creates a proveedor is stamped on it by the server, from the validated token (and, for the reception phone, the operario that the PIN identifies). The client never sends who did it.
- Permissions are enforced on the server. The client hides what a role may not do (guards, buttons) to save clicks, but the API refuses it anyway; a new endpoint is never protected only by the client.
- `Usuario` (username, nombre, `RolUsuario`, activo, password hash, security stamp) and `Operario` (nombre, activo, PIN hash) are aggregate roots with their own repositories. A password is never stored or logged: only its PBKDF2 hash (ASP.NET Core Identity's `PasswordHasher`, nothing else of Identity). A PIN is stored as HMAC-SHA256 keyed with `Pin:Secret`, never in plain text; rotating `Pin:Secret` invalidates every stored PIN and the admin must set them again.
- A user created or reset by an admin has `DebeCambiarPassword`: the server answers 403 `{ "code": "debeCambiarPassword" }` to everything except `POST /api/Auth/cambiar-password` until the user chooses a password of their own (the bootstrap admin, whose password is a deployment secret, is not forced). Operator-chosen PINs do not exist yet: the admin sets each PIN.
- The token carries the user id, role and security stamp. On every request the API checks that the user exists, is active and has the same stamp, so deactivating a user, changing or resetting a password or changing a role ends their sessions at once. Sessions last 7 days for `Recepcion` and 12 hours for the others (`Jwt:RecepcionDias`, `Jwt:PersonalHoras`); the client renews the token (`POST /api/Auth/renovar`) after half of its life.
- Login is limited per client address (`RateLimit:LoginPorMinuto`, default 30 per minute, answer 429), and a user is locked for 15 minutes after 5 wrong passwords (the answer is the same 401 as a wrong password or an unknown user, which also takes the same work). Five wrong PINs from the same user within 10 minutes block PIN checks for 5 minutes (429). An attempt (login or PIN) is counted BEFORE it is checked, through `IUsuarioRepository.ModificarAsync` (the `Usuario` row has a version and the change is retried on top of a concurrent save), so parallel guesses cannot all be checked against the same count; a right PIN gives back only its own attempt and a right password clears the login count. The client address is read from `X-Forwarded-For` trusting only the last hop of a private network (`Proxy:ForwardLimit`, `Proxy:KnownNetworks`).

## Naming

- Domain concepts stay in Spanish, matching the existing code: `Proveedor`, `RecepcionLeche`, `ParametrosCalidad`, `Crear`, `Registrar`.
- Technical suffixes and infrastructure names are in English: `Repository`, `AppService`, `Dto`, `Async`, `GetByIdAsync`.
- Follow the existing names in a file before inventing new ones.

## Domain conventions

- Entities: properties use `private set`. Each entity has a private parameterless constructor (for EF Core) and a static factory method (`Crear`, `Registrar`) that validates input.
- Value objects are `record`s with a private constructor and a static `Create` method that validates. Their properties are get-only (no `init`, no setter of any kind), so `with` cannot build an invalid instance.
- Every exception the domain raises on purpose derives from `ExcepcionDeDominio` (in `Domain/Exceptions`). A validation failure throws `ReglaDeNegocioException(mensaje, campo)` with a clear Spanish message and `nameof(parameter)` as `campo` (the field the API reports). Do not throw `ArgumentException` for a business rule: Application validation (filters, versions) uses `ReglaDeNegocioException` too. `ArgumentNullException.ThrowIfNull` stays for null arguments: that is a bug, not a rule.
- The other domain exceptions are `TicketDuplicadoException` (ticket already used) and `ConflictoDeConcurrenciaException` (stale edit of the quality limits).
- Trim strings before validating their length.
- `Proveedor` and `RecepcionLeche` are separate aggregate roots. `RecepcionLeche` references its proveedor by `ProveedorId` and is created with `RecepcionLeche.Registrar(proveedorId, ...)`; `Proveedor` holds no collection of recepciones.
- Aggregates are small; reference other aggregates by id; a collection that grows without bound never lives inside an aggregate. An architecture test checks that no domain entity exposes a collection of another aggregate root.
- "Litros a pagar" (`litros × (1 − agua% / 100)`, water null = 0) is defined once in the domain as an expression (`LitrosAPagarRegla`): `RecepcionLeche.LitrosAPagar` compiles it and `RecepcionQueries` expands it into SQL (`LitrosAPagarRegla.De` inside a projection passed through `Expandir`). Never copy the formula elsewhere on the server. Rounding to 3 decimals is applied after the query. The client copy in `utils/calidad.ts` is only the registration preview; lists and totals always show the server value.
- Collections are exposed as `IReadOnlyCollection<T>` backed by a private `List<T>`.
- Limits (max lengths, decimal precision/scale) are `public const` fields on the type that owns them, and the EF Core mapping reuses those constants.
- Plausible ranges reject typing errors and are validated in the domain (`ParametrosCalidad`, `RecepcionLeche`) and mirrored in `client/src/app/models/limits.ts`: litros 0.001-30000 (positive; the column keeps 3 decimals), grasa 1-10 %, acidez 10-30 °D, temperatura 0-45 °C, agua 0-100 %, silo 1-20 (and a new recepción's silo must also be at most the number of silos of the plant). They are different from the quality limits (`LimitesCalidad`): a plausible value outside the quality limits is accepted and flagged as out of range; an impossible value is rejected with a clear message.
- IDs are `Guid`s generated in the domain with `Guid.NewGuid()`.
- The silo of a new recepción is required (`ReglaDeNegocioException`, field `silo`) and must be within 1..`ConfiguracionCalidad.CantidadSilos`. The number of silos is data, not code: it lives in the single configuration row next to the quality limits (default 3, an Admin edits it on Configuración, plausible range 1-20, validated by `ConfiguracionCalidad` and read by `ProveedorAppService` when registering; the rule is `ConfiguracionCalidad.ValidarSilo`, never copied). Lowering the number never touches history: old recepciones keep their silo, and rows from before the silo was required keep it null and the lists show "—".
- Audit columns: `RecepcionLeche` has `RegistradoPorUsuarioId`, `OperarioId` (null when no PIN was involved) and `RegistradoEn`; `Proveedor` has `CreadoPorUsuarioId`. They reference `Usuario`/`Operario` by id (never a navigation to another aggregate) and rows from before users existed keep them null. `RegistradoEn` is a UTC audit timestamp from the `TimeProvider`; `FechaHora` (the time of the delivery) stays in plant time.
- Use `= null!` only for properties that EF Core populates; never as a general way to silence nullable warnings.

## Application conventions

- Services orchestrate: call the domain factory/methods, call the repository, map to DTOs. Business rules live in Domain, not here.
- DTOs are plain classes with `get; set;` and no validation. Input and output DTOs are separate classes.
- DTOs are shaped for their consumer: the list uses the lightweight `ProveedorDto`, there is no detail DTO with the history: `GET /api/Proveedores/{id}` returns the same `ProveedorDto` (id, nombre, count), and recepciones come only from the paged list endpoint. Proveedor counts are computed in SQL by `IProveedorQueries`; a read never materializes recepciones.
- Never return domain entities from Application. Always map to a DTO.
- Return `null` when something is not found; the controller turns it into a 404.
- Methods are `async` only when they await I/O.
- Settings classes live in `Application/Settings` and are injected as `IOptions<T>`.
- Read-side query interfaces (`IRecepcionQueries`, `IProveedorQueries`) live in `Application/Queries` and are implemented in `Infrastructure/Persistence/Queries`; they return DTOs computed in SQL and never load aggregates.
- Every "now" comes from the injected `TimeProvider` (`HoraPlanta.Ahora(reloj)` for plant time); never `DateTime.Now/UtcNow/Today` or `DateTimeOffset.Now/UtcNow` in `src`. Tests use a fixed clock (`RelojFijo`).
- Quality limits are not configuration files: they are the single `ConfiguracionCalidad` row (aggregate root, own repository), seeded with 19 °D / 20 °C / 0 % water and editable at runtime through `GET/PUT /api/Configuracion/calidad` (`ConfiguracionCalidadAppService`). The GET returns a `version` (the row version, kept in a shadow property, never a field of the entity); the PUT must send it back: without it the API answers 400 (`field: "version"`), and if someone saved in between it answers 409 (`ConflictoDeConcurrenciaException`). `RecepcionQueries` reads the current limits from the database on every request. The rule "out of range = acidity > max OR temperature > max OR water > max" lives only in the domain (`LimitesCalidad.FueraDeRango`, an expression so EF Core can translate it); never copy the thresholds or the rule elsewhere. Fat has no limit. Water null counts as 0.

## Infrastructure conventions

- One repository per aggregate root (`IProveedorRepository`, `IRecepcionRepository`, `IConfiguracionCalidadRepository`). Only aggregate roots get a `DbSet`. The foreign key from `Recepciones` to `Proveedores` is `Restrict`: recepciones are historical records and never disappear with a proveedor. Date-range queries rely on the indexes on `FechaHora` and `(ProveedorId, FechaHora)`.
- Value objects are mapped with `OwnsOne`. A child entity inside an aggregate (a small, bounded collection) would use `OwnsMany`; there are none today.
- Domain-generated IDs are configured with `ValueGeneratedNever()`.
- One `IEntityTypeConfiguration<T>` class per aggregate, in `Persistence/Configurations`, declared `internal`.
- Never edit a migration that has already been applied. Add a new migration instead (the old `Designer` files and the snapshot refer to `AppDbContext` through the project-level `<Using>` in the csproj; leave it).
- `AppDbContext` is in `DairyControl.Infrastructure.Persistence`. Concurrency tokens are shadow properties configured in the entity configuration (`IsRowVersion`), not fields of the domain entity.
- `AppDbContext` uses `EnableRetryOnFailure` (Azure SQL serverless pauses when idle). Explicit transactions must run inside `Database.CreateExecutionStrategy().ExecuteAsync(...)`; a plain `BeginTransaction` throws with a retrying strategy.

## API conventions

- Controllers are thin: call one service method and translate the result to an HTTP response. No business logic.
- Every controller has `[ApiController]` and `[Route("api/[controller]")]`.
- Every action names its policy with `[Authorize(Policy = Politicas.X)]` (`Lectura` = Admin or Oficina, which is also the default and the fallback; `LecturaCarga` adds Recepcion for the proveedor options, the next ticket and the quality limits; `Registro` = Admin, Oficina or Recepcion, to register a recepción; `Administracion` = Admin; `Autenticado`; `CambioPassword`). There is no class-level `[Authorize]` (it would be combined with the action's policy). The only anonymous endpoints are `AuthController.Login` and `/health`; a new anonymous endpoint needs an explicit decision in the plan. A new endpoint is added to the authorization matrix test (`AutorizacionTests` in `DairyControl.Api.Tests`: endpoint x role).
- For 201 responses, use a named route plus `CreatedAtRoute` (ASP.NET Core strips the `Async` suffix from action names, so `CreatedAtAction(nameof(...))` fails).
- `ExceptionHandlingMiddleware` maps `ReglaDeNegocioException` to 400 `{ "error": "...", "field": "..." }` (`field` only when there is a `campo`), `TicketDuplicadoException` (the plant ticket is unique across all proveedores), `ConflictoDeUnicidadException` (a username or an active operario PIN already taken) and `ConflictoDeConcurrenciaException` to 409 `{ "error": "..." }`, `DemasiadosIntentosException` (PIN lockout) to 429 `{ "error": "..." }`, and every other exception (including `ArgumentException` and `ArgumentNullException`: those are bugs) to a logged 500 `{ "error": "Ocurrió un error inesperado.", "traceId": "..." }`. Model-binding errors keep their own 400 path (`ApiErrors`). Do not catch domain exceptions in controllers.
- Every unexpected error is logged: the middleware logs each 500 at Error level with the exception, the request method and path (not the query string) and the trace id, and never request bodies, passwords or tokens. Do not add a `catch` that swallows an exception; if a `catch` is needed, log it (the one deliberate exception, a request aborted by the client, is logged at Debug). Every action naming an explicit policy, and `Login` and `/health` being the only anonymous endpoints, are checked by architecture tests.
- Pipeline order matters: `UseForwardedHeaders` first (the client address for the rate limiter), then the exception middleware, HTTPS redirection, CORS, `UseRateLimiter`, `UseAuthentication`, `UseAuthorization`, `MapControllers`.

## Client conventions

- Standalone components only (no NgModules). Use signals for component state and the built-in control flow (`@if`, `@for`, `@empty`).
- Folders under `client/src/app`:
  - `pages/`: routed components (`login`, `resumen`, `recepciones`, `proveedores-list`, `proveedor-detail`, `configuracion`, `carga-rapida`, `inicio`, `cambiar-password`, `usuarios`)
  - `components/`: reusable components and dialogs (`crear-proveedor-dialog`, `usuario-dialog`, `operario-dialog`, `valor-secreto-dialog`, `confirmar-accion-dialog`, `numero-input` (the `appNumero` directive), `observacion-celda`, `observaciones-dialog`, `recepciones-filtros`, `recepciones-list`, `recepciones-panel`)
  - `services/`: the only place that uses `HttpClient` (`ProveedoresService`, `RecepcionesService`, `ConfiguracionService`, `AuthService`, `UsuariosService`)
  - `models/`: TypeScript interfaces mirroring the API DTOs, plus `limits.ts`
  - `guards/`, `interceptors/`, `utils/`
- Components get dependencies with `inject()`. Components never call `HttpClient` directly.
- `provideHttpClient(...)` is called only in `app.config.ts`.
- Signal inputs (`input()`) are reactive: react to changes with `effect()` or `computed()`; never read them only once in `ngOnInit`.
- Numeric fields use the `appNumero` directive (a text input with `inputmode`, never `type="number"`, which reinterprets "1.234,5" as 1.2345); `appNumero="entero"` for integers (ticket, number of silos). es-AR rules ("." thousands, "," decimals, ambiguous input rejected with a message) live only in `utils/numero-es.ts`; the form value and the API stay plain numbers. Integer identifiers (ticket, silo) are shown without separators.
- Forms are reactive forms. Client validation mirrors the domain limits in `models/limits.ts`; it exists for user feedback only, and the API remains the source of truth.
- The registration form may preview the quality limits for UX (the limit values are read from the API, never hardcoded) and the real liters (`litrosReales` in `utils/calidad.ts`); the domain and the API remain the source of truth, and lists and totals show the server values.
- API errors are turned into user messages with `getApiErrorMessage` in `utils/api-error.ts`.
- Authentication: only `AuthService` reads or writes the token and exposes the role (signals `rol`, `esAdmin`, `esRecepcion`, `inicio`). `authInterceptor` adds the `Authorization` header (never add it manually), sends a 401 to the login page and a 403 `debeCambiarPassword` to `/cambiar-password`. Routes use `roleGuard(...roles)`; each role starts at its own page (`/inicio` for the reception phone, `/resumen` for the rest) and an unknown address goes there. The client only hides what a role may not do; the API decides. The reception phone has no toolbar, tabs, settings or logout. The client never keeps a PIN after a send and shows it masked with a numeric keyboard.
- `slowRequestInterceptor` and `ServerStatusService` show the global "waking up" banner when any request takes more than 3 seconds; pages don't add their own cold-start messages.
- The API base URL comes from `environments/`; never hardcode it.
- User-facing text is in Spanish.

### UI conventions

- Registration flow (`/recepciones/carga`, page `carga-rapida`): the only way to register a recepción (there is no registration dialog and no second form). Three steps (load, confirm, done) on the `POST`, with the form definition in `pages/carga-rapida/formulario-recepcion.ts`; for Admin, Oficina and the reception phone. "Registrar recepción" on the Recepciones page and on a proveedor's page link to it (`?proveedor=<id>` preselects the proveedor). Phone-first; at 600 px and wider it is a centered card of at most 560 px. The silo is chosen with large buttons 1..N (N is the number of silos of the plant, read from the API; the buttons are not shown before it arrives) and is required; the ticket is required too. Touch targets are at least 48 px. Fecha/hora is fixed when entering the confirmation and sent unchanged, also on a retry. After a network error, a 409 on the retry means "probably already saved" and is never shown as a duplicate ticket. The done step shows the values as entered and, for Admin and Oficina, a link back to Recepciones.

- Styling: each component's styles live in its own CSS file; global styles and theme in `styles.scss`.
- Form fields: mark optional fields in the label, e.g. "Grasa (%) (opcional)"; required fields keep the asterisk. Never use `mat-hint` for "opcional"; hints are only for short help text. Leave room for hints and errors so they never overlap the next field's label (form fields use dynamic subscript sizing from `app.config.ts`, and outline fields have a bottom margin in `styles.scss`).
- No page-level horizontal scroll at any width. Main sections are tabs; rarely used pages (settings) are icon buttons in the top row. The tabs row may scroll inside itself, never the page.
- Tables:
  - Layout: fixed layout with percentage column widths (one column takes the rest), inside an `overflow-x: auto` container. The table fits its card on desktop without horizontal scroll. Below its `min-width` (860px) it scrolls horizontally with a visible, styled scrollbar, only on narrow screens.
  - Headers: short, one line, with the full name in a `matTooltip` and `aria-label`. Never let headers wrap or truncate.
  - Cells: single line; long text is truncated with an ellipsis and its full text in a `matTooltip`. Text that matters but does not fit (observaciones) is a real button (keyboard accessible, with a "ver más" affordance only when it is truncated) that opens a dialog with the full text and its context.
  - Alignment and padding: headers and cells are vertically centered with the same vertical padding; the first and last cells have the same horizontal padding, so text never touches a border or the card edge; quantities (liters, percentages, measurements) are right-aligned, identifiers (ticket, silo, codes) are centered and text is left-aligned; each header is aligned like its column.
- Visual check: for any UI change, before reporting back, run the app and take Playwright screenshots of every changed page at 1366px and 390px wide. Check them yourself (no clipped or truncated text, no unexpected scroll, nothing touching the edges, consistent alignment), fix what is wrong, and include the screenshots in your report (in a PR, see PR evidence in the Git workflow). Launch Chromium with `ignoreDefaultArgs: ['--hide-scrollbars']`, otherwise scrollbars are invisible in the screenshots.
- A control that needs data from the API to work (the options of a select, a suggestion) is not offered until the data is there: show a disabled "Cargando…" placeholder, never an empty control. An empty `mat-select` ignores the tap without any error, so a tap on a slow connection (or a click in a fast test) does nothing and nobody knows why. (Root cause of the e2e failure of PR #4: the quick flow's proveedor select.)

## Testing

- xUnit with Moq. Arrange-Act-Assert, with the three sections commented.
- Test names: `Method_Scenario_ExpectedResult`.
- Domain tests use real objects and no mocks.
- Application tests mock the repositories and queries (`IProveedorRepository`, `IRecepcionRepository`, `IProveedorQueries`), created inside each test (`var mockRepo = new Mock<IProveedorRepository>();`). Domain, Application and Api tests never touch a database.
- `DairyControl.Application.Tests` does not reference Infrastructure; tests of Infrastructure types (`UnicidadSql`, `DemoDataGenerator`, the seeder, the repositories) live in `DairyControl.Infrastructure.Tests`.
- Only `DairyControl.Infrastructure.Tests` may use a database, and only an in-memory SQLite one (schema from `EnsureCreated`, since the migrations are SQL Server-specific), for the reset/seeder logic.
- SQLite tests don't cover SQL Server-specific behavior (migrations, unique-violation error codes, query translation); those are verified manually against SQL Server until we add Testcontainers integration tests.
- Verify persistence calls with `Verify(..., Times.Once)` or `Times.Never`, not just the returned data.
- Call `Assert.NotNull(result)` before asserting on properties of a nullable result.

### Architecture tests

- `tests/DairyControl.ArchitectureTests` (NetArchTest.Rules, reflection and a scan of the `src` sources) fails the build when a rule of this file is broken: Domain references only the BCL; Application does not use Infrastructure, EF Core or ASP.NET Core; Infrastructure does not use Api; controllers do not use `AppDbContext` or the repositories; entities have no public setters; value objects have no setters; every exception in `DairyControl.Domain.Exceptions` derives from `ExcepcionDeDominio`; no `new ArgumentException` or `ArgumentException.ThrowIf...` anywhere in `src` (`ArgumentNullException` is allowed); every controller action names an explicit policy of `Politicas` (or is `Login`), no controller has a class-level `[Authorize]`, the only `[AllowAnonymous]` action is `AuthController.LoginAsync` and the only `.AllowAnonymous()` mapping is `/health`; no direct clock reads in `src`.
- When a rule of this file can be checked by a test, add the test in the same PR that adds the rule.

### E2E tests

- `e2e/` holds Playwright tests (TypeScript) that run against the Docker Compose stack, in the CI job `e2e` (required check). `global-setup.ts` waits for `GET /health` (anonymous, 200 when the API and the database are reachable); never use fixed sleeps.
- Each test creates its own proveedor and a unique ticket through the UI, so tests depend neither on each other nor on the seed. A test of a role creates its own user (and operario with a free PIN) through the admin API (`crearUsuarioListoPorApi`, `crearOperarioPorApi`); the compose stack raises the login limit for the suite. Tests that change the global quality limits restore them in `finally`; that is why they run with one worker.
- Selectors are roles and labels (`getByRole`, `getByLabel`), never CSS classes.
- A new critical flow gets an e2e test in the same PR.
- CI is several times slower than a developer machine, and the first request after the API starts is the slowest. A test never acts on a control the instant a page opens: it waits for the control to be ready with a web-first assertion (`toBeEnabled`, `toBeVisible`, or a role that only exists once the data is loaded). A race like that passes locally by luck, so `e2e/fixtures.ts` delays every `/api` response when `E2E_API_LATENCY_MS` is set, and CI runs the specs of the flows that load data when they open (`carga-rapida`, `roles`) a second time with `E2E_API_LATENCY_MS=400`. Run the same command locally before a PR that adds or changes such a flow or a form that loads its own options, and put new flows of that kind in the list in `ci.yml`.
- When the `e2e` check fails, read the evidence before changing anything: `gh run view <run> --log-failed` for the test error, the `playwright-report` artifact (error context and trace network log) for the page state and request timings, and the filtered API warnings that CI prints before the full compose logs.

### Exploratory checks

Before opening every PR, run these with Playwright against the running app (`docker compose up --build`) on every form, field and endpoint the PR adds or changes, and report the results in the PR's Self-review section (what was checked, what was found, what was fixed). The script is throwaway and is not committed; if a check finds a bug, add a regression test (unit or e2e) with the fix.

- Empty and whitespace-only fields.
- Negative, zero and huge numbers.
- Letters in numeric fields.
- "3,5" vs "3.5" for decimals.
- 5,000-character text.
- Accents and emojis.
- HTML/script and SQL-like text: it must be shown as text and never executed.
- Future and very old dates.
- Double click on submit: exactly one record is created.
- Expired or invalid session: redirect to login, no stuck state.

Fix small findings in the same PR. For anything bigger, propose a separate issue to the developer and list it in the Self-review; do not open it without approval.

## Security

- Secrets live in .NET User Secrets: `Jwt:SigningKey`, `Pin:Secret` (at least 32 characters; the API does not start without it) and `AdminUser:Password` (only used to create the first Admin when there are no users). Never write a secret into `appsettings.json` or any committed file. The client build contains no credential.
- Compare secrets with `CryptographicOperations.FixedTimeEquals`, not `==`.

## Commands (run from the repo root unless noted)

- Build: `dotnet build DairyControl.slnx`
- Test: `dotnet test --solution DairyControl.slnx`
- Run the API: `dotnet run --project src/DairyControl.Api`
- Add a migration: `dotnet ef migrations add <Name> --project src/DairyControl.Infrastructure --startup-project src/DairyControl.Api`
- Apply migrations: `dotnet ef database update --project src/DairyControl.Infrastructure --startup-project src/DairyControl.Api`
- Client (from `client/`): `npm install`, `ng serve` (http://localhost:4200), `ng build`
- E2E (from `e2e/`, with the stack running): `npm ci`, `npx playwright install chromium`, `npm test`
- Full stack in Docker: `docker compose up --build` (http://localhost:8080; needs a `.env` copied from `.env.example`)
- Deployment: `.github/workflows/deploy.yml` is disabled in this repo until the production instance exists; do not edit, re-enable or run it. When enabled it runs after CI passes on `main`: builds both images, pushes them to ghcr.io tagged with the short commit SHA, and updates both Azure container apps. Azure login uses OIDC; no credentials are stored in the repo or the workflow.
- Stop the stack and reset the database: `docker compose down -v`
- Health: `GET /health` on the API (through nginx at http://localhost:8080/health): 200 when the API and the database are reachable, 503 otherwise. See `docs/deployment.md`.

### Docker / configuration flags

- The compose stack is configured only through environment variables in `.env` (`ConnectionStrings__Default`, `Jwt__SigningKey`, `AdminUser__Password`, `Pin__Secret`), not User Secrets. Local development keeps using LocalDB and User Secrets.
- `Database:ApplyMigrationsOnStartup` (default `false`): when `true`, the API runs `Database.Migrate()` at startup. Enabled only in `compose.yaml`.
- `HttpsRedirection:Enabled` (default `true`): when `false`, `UseHttpsRedirection` is skipped. Set to `false` only in `compose.yaml`, where the API listens on plain HTTP behind nginx.
- `DemoData:SeedOnStartup` (default `false`): when `true` and the database has no proveedores, `DemoDataSeeder` creates demo data at startup. Enabled only in `compose.yaml`.
- `DemoData:ResetOnStartup` (default `false`): when `true`, at startup (after migrations, before seeding) the API deletes all recepciones and proveedores, restores the default quality limits (`LimitesCalidad.Predeterminados`) and reseeds the demo data, in one transaction inside the execution strategy; it logs a warning with the deleted counts and never touches users or operarios (the demo rows are credited to the first admin). It reseeds even if `SeedOnStartup` is `false`. It runs on every API start (cold start, new revision, restart), not only after scaling to zero. Only for the demo environment (Azure); not enabled in `compose.yaml`.
- `API_UPSTREAM` (client image, default `http://api:8080`): where nginx proxies `/api`. `client/nginx.conf.template` is filled from environment variables when the container starts. On Azure it points to the API's internal ingress.

## How to work in this repo

- Start with a plan that lists every file you will create or change. Wait for approval before editing.
- Keep changes scoped to the task. Do not refactor unrelated code. If a new file is needed, justify it in the plan.
- After making changes, run the build and the tests (and `ng build` for client changes), and report the results.
- For UI changes, also do the visual check described in Client conventions > UI conventions.
- Before every PR, run the Exploratory checks (Testing > Exploratory checks) and the e2e tests locally on a fresh stack (`docker compose down -v && docker compose up --build -d`), including the slow-API run (see E2E tests) when the change touches a flow that loads data when it opens.
- If a request conflicts with a rule in this file, stop and ask.
- Every bug that escapes to `main` gets a regression test and, when it is a class of error (it could happen again elsewhere), a rule in this file or an architecture test, in the same PR as the fix.
- Run the architecture review (`docs/architecture-review.md`) every 5 merged PRs and before any freeze or release: a fresh subagent reviews `main` with that checklist and this file, ranks the findings by severity and proposes issues (do not open them without approval). Every recurring class of finding becomes an architecture test or a rule here. Record each review in `docs/reviews/<yyyy-mm-dd>.md`.
- Git: follow the Git workflow below.

## Git workflow

Claude handles branches, commits, pushes and pull requests; the developer reviews on GitHub and merges.

- Before starting an issue: `git switch main`, `git pull`, then create `feature/<issue>-<short-desc>` (e.g. `feature/53-reset-demo-data`). If the developer already created the branch, use it.
- Commit in small logical units, one concern per commit, with imperative English messages ("Add demo reset flag"). Never commit secrets, `.env` or real plant data.
- Self-review before every commit: read the full staged diff (`git diff --cached`) and check it against the issue's acceptance criteria, the CLAUDE.md conventions, tests for new behavior, leftover debug code or TODOs, and secrets. Fix what you find before committing.
- Independent review before the PR: run a fresh subagent that has not seen the implementation conversation. Give it only the issue text, `CLAUDE.md` and `git diff main...HEAD`, and ask for bugs, risks, missing tests and convention breaks, ranked by severity. Fix every real finding (as new commits), then rerun the build, the tests and the visual check.
- Open the PR when the build, tests, visual check and independent review are done: push the branch (`git push -u origin <branch>`) and open it with `gh pr create`. Title `Feature <N>/<issue title>` (N is the issue number); body with Summary / Changes / Evidence / Self-review / Notes (`.github/pull_request_template.md`) and `Closes #<N>`.
- PR evidence: screenshots (demo app, synthetic data only; never real plant data) go to the `pr-evidence` branch, folder `pr-<N>/` (here N is the PR number, so publish them after the PR exists). Use a git worktree at `artifacts/wt-evidence` inside the repo (the `artifacts/` folder is git-ignored; never a system temp folder) so the feature branch is not touched, and remove it with `git worktree remove` once the PR body is updated. If `pr-evidence` does not exist, create it as an orphan branch (`git worktree add --orphan -b pr-evidence <dir>`) with a short README saying it is never merged. Embed each image in the PR body by commit SHA: `![<name>](https://github.com/isaias-muga/Control-Diario-Planta/blob/<sha>/pr-<N>/<file>.png?raw=true)` (`raw.githubusercontent.com` links do not render in a private repo; this form does for logged-in collaborators), then update the body with `gh pr edit <N> --body-file <file>`.
- The PR body has a Self-review section: what the independent review found and how each finding was fixed (or why it was rejected), plus the Exploratory checks results.
- The PR body has a Notes section that records the process numbers: lead time (issue opened to PR ready), what the independent review, the E2E tests and the Exploratory checks each caught, and the number of review rounds (update it when review rounds happen).
- Review changes: the developer comments on the PR or tells Claude. Fix them on the same branch, push, and reply with what changed.
- Never: merge PRs, push to `main`, force push, rebase or reset shared branches, delete remote branches, change GitHub settings. `.claude/settings.json` denies the riskiest commands as a safety net (its rules match the command text anywhere, so avoid quoting a denied command in a commit message), but the rule holds even where no deny rule matches (for example a bare `git push` while on `main`).
- After the developer merges: `git switch main`, `git pull`, delete the local branch (`git branch -d <branch>`).

## Never do

- Public setters on domain entities, and setters or `init` on value objects.
- `throw new ArgumentException` for a business rule (use `ReglaDeNegocioException`), or a `catch` that swallows an exception without logging it.
- `DateTime.Now/UtcNow/Today` or `DateTimeOffset.Now/UtcNow` in `src` (use `TimeProvider`).
- A `DbSet` for anything inside an aggregate.
- Business logic in controllers, DTOs or Angular components.
- Domain entities returned from Application or Api.
- `HttpClient` used outside `services/`.
- Secrets in committed files.
- Edits to already-applied migrations.
- Deleting `global.json` or pinning an SDK version in it.
