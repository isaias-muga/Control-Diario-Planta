using DairyControl.Api.Startup;
using Microsoft.Extensions.Logging;

namespace DairyControl.Api.Tests
{
    public class PasoDeArranqueTests
    {
        private sealed class LoggerGrabador : ILogger
        {
            public List<(LogLevel Nivel, string Mensaje, Exception? Excepcion)> Entradas { get; } = [];

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                Entradas.Add((logLevel, formatter(state, exception), exception));
        }

        [Fact]
        public async Task EjecutarAsync_StepSucceeds_RunsItAndLogsNothing()
        {
            // Arrange
            var logger = new LoggerGrabador();
            var corrio = false;

            // Act
            await PasoDeArranque.EjecutarAsync(logger, "aplicar las migraciones", () =>
            {
                corrio = true;
                return Task.CompletedTask;
            });

            // Assert
            Assert.True(corrio);
            Assert.Empty(logger.Entradas);
        }

        [Fact]
        public async Task EjecutarAsync_StepFails_LogsCriticalWithTheStepNameAndRethrowsTheSameException()
        {
            // Arrange
            var logger = new LoggerGrabador();
            var error = new InvalidOperationException("no se pudo conectar");

            // Act
            var lanzada = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PasoDeArranque.EjecutarAsync(logger, "aplicar las migraciones", () => throw error));

            // Assert
            Assert.Same(error, lanzada);
            var entrada = Assert.Single(logger.Entradas);
            Assert.Equal(LogLevel.Critical, entrada.Nivel);
            Assert.Contains("aplicar las migraciones", entrada.Mensaje);
            Assert.Same(error, entrada.Excepcion);
        }

        [Fact]
        public async Task EjecutarAsync_AsyncStepFails_AlsoLogsCriticalAndRethrows()
        {
            // Arrange
            var logger = new LoggerGrabador();

            // Act
            await Assert.ThrowsAsync<TimeoutException>(() =>
                PasoDeArranque.EjecutarAsync(logger, "crear el primer administrador", async () =>
                {
                    await Task.Yield();
                    throw new TimeoutException();
                }));

            // Assert
            var entrada = Assert.Single(logger.Entradas);
            Assert.Equal(LogLevel.Critical, entrada.Nivel);
            Assert.Contains("crear el primer administrador", entrada.Mensaje);
        }
    }
}
