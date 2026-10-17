using DairyControl.Api.Authorization;
using DairyControl.Api.Health;
using DairyControl.Api.Middleware;
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

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

// The first admin comes from the deployment secret when there are no users yet (not forced to change it: the deployer chose it)
if (!string.IsNullOrEmpty(app.Configuration["AdminUser:Password"]))
{
    using var scope = app.Services.CreateScope();
    if (await scope.ServiceProvider.GetRequiredService<UsuarioAppService>().AsegurarAdminInicialAsync())
    {
        scope.ServiceProvider.GetRequiredService<ILogger<UsuarioAppService>>().LogWarning("Primer administrador creado desde AdminUser:Password.");
    }
}

var resetDemoData = app.Configuration.GetValue<bool>("DemoData:ResetOnStartup");
var seedDemoData = app.Configuration.GetValue<bool>("DemoData:SeedOnStartup");
if (resetDemoData || seedDemoData)
{
    using var scope = app.Services.CreateScope();
    var seeder = new DemoDataSeeder(
        scope.ServiceProvider.GetRequiredService<AppDbContext>(),
        scope.ServiceProvider.GetRequiredService<TimeProvider>(),
        scope.ServiceProvider.GetRequiredService<ILogger<DemoDataSeeder>>());
    await seeder.InitializeAsync(resetDemoData, seedDemoData);
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

app.Run();

// The authorization tests start the real application in memory
public partial class Program;