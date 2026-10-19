using DairyControl.Application.Settings;
using DairyControl.Domain.Entities;

namespace DairyControl.Api.Startup
{
    /// <summary>
    /// The configuration the API cannot run without, checked before the host is built so a bad deployment stops at once with every
    /// problem named, instead of failing on the first login or request. The messages name the key and the minimum, never a value.
    /// </summary>
    public static class ConfiguracionDeArranque
    {
        public static IReadOnlyList<string> Validar(IConfiguration configuracion)
        {
            ArgumentNullException.ThrowIfNull(configuracion);
            var problemas = new List<string>();

            if (string.IsNullOrWhiteSpace(configuracion.GetConnectionString("Default")))
            {
                problemas.Add("Falta ConnectionStrings:Default (la cadena de conexión a la base de datos).");
            }
            ExigirLargo(configuracion, "Jwt:SigningKey", JwtSettings.SigningKeyMinLength, problemas);
            ExigirLargo(configuracion, "Pin:Secret", PinSettings.SecretMinLength, problemas);
            ExigirLargo(configuracion, "AdminUser:Password", Usuario.PasswordMinLength, problemas);

            return problemas;
        }

        private static void ExigirLargo(IConfiguration configuracion, string clave, int minimo, List<string> problemas)
        {
            var valor = configuracion[clave];
            if (string.IsNullOrEmpty(valor))
            {
                problemas.Add($"Falta {clave}: debe tener al menos {minimo} caracteres.");
            }
            else if (valor.Length < minimo)
            {
                problemas.Add($"{clave} es demasiado corto: debe tener al menos {minimo} caracteres.");
            }
        }

        /// <summary>Logs every problem as Critical with a console logger (the host and its logging do not exist yet) and stops the start.</summary>
        public static void ExigirValida(IConfiguration configuracion)
        {
            var problemas = Validar(configuracion);
            if (problemas.Count == 0)
            {
                return;
            }
            using var fabrica = LoggerFactory.Create(logging => logging.AddSimpleConsole());
            var logger = fabrica.CreateLogger("DairyControl.Arranque");
            foreach (var problema in problemas)
            {
                logger.LogCritical("Configuración inválida: {Problema}", problema);
            }
            throw new InvalidOperationException($"La API no puede arrancar: {problemas.Count} problema(s) de configuración. Ver el log.");
        }
    }
}
