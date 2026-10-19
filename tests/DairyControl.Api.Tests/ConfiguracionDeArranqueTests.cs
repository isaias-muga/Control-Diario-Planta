using DairyControl.Api.Startup;
using Microsoft.Extensions.Configuration;

namespace DairyControl.Api.Tests
{
    public class ConfiguracionDeArranqueTests
    {
        private const string ClaveLarga = "0123456789abcdef0123456789abcdef"; // 32 characters

        private static IConfiguration Configuracion(params (string Clave, string? Valor)[] valores) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Clave, v.Valor)))
                .Build();

        private static (string Clave, string? Valor)[] Completa() =>
        [
            ("ConnectionStrings:Default", "Server=db;Database=x"),
            ("Jwt:SigningKey", ClaveLarga),
            ("Pin:Secret", ClaveLarga),
            ("AdminUser:Password", "contraseña-larga"),
        ];

        private static IConfiguration Con(string clave, string? valor) =>
            Configuracion(Completa().Select(v => v.Clave == clave ? (v.Clave, valor) : v).ToArray());

        [Fact]
        public void Validar_EverythingPresent_HasNoProblems()
        {
            // Arrange
            var configuracion = Configuracion(Completa());

            // Act
            var problemas = ConfiguracionDeArranque.Validar(configuracion);

            // Assert
            Assert.Empty(problemas);
        }

        [Theory]
        [InlineData("ConnectionStrings:Default", null)]
        [InlineData("ConnectionStrings:Default", "   ")]
        [InlineData("Jwt:SigningKey", null)]
        [InlineData("Jwt:SigningKey", "")]
        [InlineData("Jwt:SigningKey", "0123456789abcdef0123456789abcde")] // 31 characters
        [InlineData("Pin:Secret", null)]
        [InlineData("Pin:Secret", "0123456789abcdef0123456789abcde")] // 31 characters
        [InlineData("AdminUser:Password", null)]
        [InlineData("AdminUser:Password", "1234567")] // 7 characters
        public void Validar_OneSettingMissingOrTooShort_ReportsThatKey(string clave, string? valor)
        {
            // Arrange
            var configuracion = Con(clave, valor);

            // Act
            var problemas = ConfiguracionDeArranque.Validar(configuracion);

            // Assert
            var problema = Assert.Single(problemas);
            Assert.Contains(clave, problema);
        }

        [Theory]
        [InlineData("Jwt:SigningKey", "0123456789abcdef0123456789abcdef")] // exactly 32
        [InlineData("Pin:Secret", "0123456789abcdef0123456789abcdef")] // exactly 32
        [InlineData("AdminUser:Password", "12345678")] // exactly 8
        public void Validar_ExactlyTheMinimumLength_IsValid(string clave, string valor)
        {
            // Arrange
            var configuracion = Con(clave, valor);

            // Act
            var problemas = ConfiguracionDeArranque.Validar(configuracion);

            // Assert
            Assert.Empty(problemas);
        }

        [Fact]
        public void Validar_NothingConfigured_ReportsEveryProblemAtOnce()
        {
            // Arrange
            var configuracion = Configuracion();

            // Act
            var problemas = ConfiguracionDeArranque.Validar(configuracion);

            // Assert
            Assert.Equal(4, problemas.Count);
        }

        [Fact]
        public void Validar_TooShortSecrets_NeverRepeatsTheirValue()
        {
            // Arrange
            const string secretoCorto = "secreto-corto-xyz";
            var configuracion = Configuracion(
                ("ConnectionStrings:Default", "Server=db;Password=clave-de-la-base"),
                ("Jwt:SigningKey", secretoCorto),
                ("Pin:Secret", secretoCorto),
                ("AdminUser:Password", "corta"));

            // Act
            var problemas = ConfiguracionDeArranque.Validar(configuracion);

            // Assert
            Assert.Equal(3, problemas.Count);
            Assert.All(problemas, p =>
            {
                Assert.DoesNotContain(secretoCorto, p);
                Assert.DoesNotContain("corta", p);
                Assert.DoesNotContain("clave-de-la-base", p);
            });
        }

        [Fact]
        public void ExigirValida_BadConfiguration_ThrowsWithTheNumberOfProblems()
        {
            // Arrange
            var configuracion = Con("Jwt:SigningKey", null);

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => ConfiguracionDeArranque.ExigirValida(configuracion));

            // Assert
            Assert.Contains("1 problema", ex.Message);
        }

        [Fact]
        public void ExigirValida_GoodConfiguration_DoesNotThrow()
        {
            // Arrange
            var configuracion = Configuracion(Completa());

            // Act
            var ex = Record.Exception(() => ConfiguracionDeArranque.ExigirValida(configuracion));

            // Assert
            Assert.Null(ex);
        }
    }
}
