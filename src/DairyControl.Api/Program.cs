using DairyControl.Api.Authorization;
using DairyControl.Api.Health;
using DairyControl.Api.Middleware;
using DairyControl.Api.Startup;
using DairyControl.Application;
using DairyControl.Application.Seguridad;
using DairyControl.Application.Services;
using DairyControl.Application.Settings;
using DairyControl.Infrastructure;
using DairyControl.Infrastructure.Persistence;
using DairyControl.Api.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Stop at once, naming every missing or too-short setting, instead of failing on the first login (the host has no logger yet)
ConfiguracionDeArranque.ExigirValida(builder.Configuration);

// 1. Registering services — telling the DI container what exists
builder.Services.AddControllers(options => ApiErrors.UseSpanishBindingMessages(options))
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ApiErrors.InvalidModelState);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration.GetConnectionString("Default"));
builder.Services.AddDairyControlHealthChecks();

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<AdminUserSettings>(builder.Configuration.GetSection("AdminUser"));

builder.Services.AddOptions<PinSettings>()
    .Bind(builder.Configuration.GetSection("Pin"))
    .Validate(pin => pin.Secret.Length >= PinSettings.SecretMinLength, $"Pin:Secret debe tener al menos {PinSettings.SecretMinLength} caracteres.")
    .ValidateOnStart();

builder.Services.AddDairyControlAuthentication(builder.Configuration);
builder.Services.AddDairyControlAuthorization();
builder.Services.AddScoped<IUsuarioActual, UsuarioActualHttp>();
builder.Services.AddDairyControlProteccionDeAcceso();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Client", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// Every startup step that can fail logs a Critical line with its name before the exception goes on
if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await PasoDeArranque.EjecutarAsync(app.Logger, "aplicar las migraciones", () =>
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
        return Task.CompletedTask;
    });
}

// The first admin comes from the deployment secret when there are no users yet; unless AdminUser:ForzarCambioPassword is false
// (compose, e2e, the demo) it must choose a password of its own at the first login
await PasoDeArranque.EjecutarAsync(app.Logger, "crear el primer administrador", async () =>
{
    using var scope = app.Services.CreateScope();
    if (await scope.ServiceProvider.GetRequiredService<UsuarioAppService>().AsegurarAdminInicialAsync())
    {
        scope.ServiceProvider.GetRequiredService<ILogger<UsuarioAppService>>().LogWarning("Primer administrador creado desde AdminUser:Password.");
    }
});

var resetDemoData = app.Configuration.GetValue<bool>("DemoData:ResetOnStartup");
var seedDemoData = app.Configuration.GetValue<bool>("DemoData:SeedOnStartup");
if (resetDemoData || seedDemoData)
{
    await PasoDeArranque.EjecutarAsync(app.Logger, "cargar o reiniciar los datos de demostración", async () =>
    {
        using var scope = app.Services.CreateScope();
        var seeder = new DemoDataSeeder(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            scope.ServiceProvider.GetRequiredService<ILogger<DemoDataSeeder>>());
        await seeder.InitializeAsync(resetDemoData, seedDemoData);
    });
}

// 2. The pipeline — what happens to every incoming request, in order
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (app.Configuration.GetValue("HttpsRedirection:Enabled", true))
{
    app.UseHttpsRedirection();
}
app.UseCors("Client");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapDairyControlHealth();

// A failure while the host starts (for example the Pin:Secret validation) is logged Critical too, not only left to the crash output
await PasoDeArranque.EjecutarAsync(app.Logger, "iniciar el servidor", () => app.RunAsync());

// The authorization tests start the real application in memory
public partial class Program;