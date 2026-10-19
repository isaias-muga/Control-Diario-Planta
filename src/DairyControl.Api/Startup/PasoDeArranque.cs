namespace DairyControl.Api.Startup
{
    /// <summary>
    /// Runs one startup step (migrations, the first admin, demo data) and, if it fails, logs it as Critical with the step's name before
    /// letting the exception go on: a deployment that cannot start must say which step broke, and the host's crash output alone does not.
    /// </summary>
    public static class PasoDeArranque
    {
        public static async Task EjecutarAsync(ILogger logger, string nombre, Func<Task> paso)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(paso);
            try
            {
                await paso();
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Falló el arranque en el paso: {Paso}.", nombre);
                throw;
            }
        }
    }
}
