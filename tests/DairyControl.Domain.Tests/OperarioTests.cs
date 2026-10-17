using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;

namespace DairyControl.Domain.Tests
{
    public class OperarioTests
    {
        [Fact]
        public void Crear_ValidData_IsActive()
        {
            // Arrange & Act
            var operario = Operario.Crear("  Juan Pérez ", "hash-pin");

            // Assert
            Assert.NotEqual(Guid.Empty, operario.Id);
            Assert.Equal("Juan Pérez", operario.Nombre);
            Assert.Equal("hash-pin", operario.PinHash);
            Assert.True(operario.Activo);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Crear_BlankNombre_ThrowsReglaDeNegocioException(string nombre)
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Operario.Crear(nombre, "hash-pin"));

            // Assert
            Assert.Equal("nombre", ex.Campo);
        }

        [Fact]
        public void Crear_NombreTooLong_ThrowsReglaDeNegocioException()
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Operario.Crear(new string('a', Operario.NombreMaxLength + 1), "hash-pin"));

            // Assert
            Assert.Equal("nombre", ex.Campo);
        }

        [Fact]
        public void Crear_EmptyHash_ThrowsReglaDeNegocioException()
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Operario.Crear("Juan", ""));

            // Assert
            Assert.Equal("pin", ex.Campo);
        }

        [Theory]
        [InlineData("0000")]
        [InlineData("1234")]
        [InlineData("9999")]
        public void ValidarPin_FourDigits_IsAccepted(string pin)
        {
            // Act & Assert
            Operario.ValidarPin(pin);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("123")]
        [InlineData("12345")]
        [InlineData("12a4")]
        [InlineData("12 4")]
        [InlineData("-123")]
        [InlineData("١٢٣٤")]
        public void ValidarPin_NotExactlyFourAsciiDigits_ThrowsReglaDeNegocioException(string? pin)
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Operario.ValidarPin(pin));

            // Assert
            Assert.Equal("pin", ex.Campo);
        }

        [Fact]
        public void Desactivar_ActiveOperario_BecomesInactive()
        {
            // Arrange
            var operario = Operario.Crear("Juan", "hash-pin");

            // Act
            operario.Desactivar();

            // Assert
            Assert.False(operario.Activo);
        }

        [Fact]
        public void CambiarPin_NewHash_ReplacesTheHash()
        {
            // Arrange
            var operario = Operario.Crear("Juan", "hash-1");

            // Act
            operario.CambiarPin("hash-2");

            // Assert
            Assert.Equal("hash-2", operario.PinHash);
        }

        [Fact]
        public void CambiarPin_EmptyHash_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var operario = Operario.Crear("Juan", "hash-1");

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => operario.CambiarPin(" "));

            // Assert
            Assert.Equal("pin", ex.Campo);
        }
    }
}
