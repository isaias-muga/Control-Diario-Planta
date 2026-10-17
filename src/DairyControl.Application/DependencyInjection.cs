using DairyControl.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DairyControl.Application;

public static class DependencyInjection
{
    /// <summary>Registers the application services and the clock they share (a host or a test can register its own TimeProvider first).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ProveedorAppService>();
        services.AddScoped<ResumenRecepcionesAppService>();
        services.AddScoped<ListadoRecepcionesAppService>();
        services.AddScoped<ConfiguracionCalidadAppService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UsuarioAppService>();
        services.AddScoped<OperarioAppService>();
        services.AddScoped<VerificacionPinService>();
        return services;
    }
}
