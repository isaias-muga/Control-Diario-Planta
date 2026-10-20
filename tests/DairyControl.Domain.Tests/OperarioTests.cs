using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Domain.Tests
{
    public class OperarioTests
    {
        private static readonly DateTime Ahora = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);
        private static readonly Guid AdminId = Guid.NewGuid();
        private static readonly MotivoDeCambio Motivo = MotivoDeCambio.Create("Dejó de trabajar");

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
            var cambio = operario.Desactivar(Motivo, AdminId, Ahora);

            // Assert
            Assert.False(operario.Activo);
            Assert.Equal(CambioRegistrado.EntidadOperario, cambio.Entidad);
            Assert.Equal(operario.Id, cambio.EntidadId);
            Assert.Equal(CambioRegistrado.Campos.Activo, cambio.Campo);
            Assert.Equal("Activo", cambio.ValorAnterior);
            Assert.Equal("Inactivo", cambio.ValorNuevo);
            Assert.Equal(AdminId, cambio.UsuarioId);
            Assert.Equal(Ahora, cambio.EnUtc);
            Assert.Equal("Dejó de trabajar", cambio.Motivo);
        }

        [Fact]
        public void Desactivar_AlreadyInactive_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var operario = Operario.Crear("Juan", "hash-pin");
            operario.Desactivar(Motivo, AdminId, Ahora);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => operario.Desactivar(Motivo, AdminId, Ahora));

            // Assert
            Assert.Equal("id", ex.Campo);
        }

        [Fact]
        public void Reactivar_InactiveOperario_BecomesActiveAndKeepsItsPin()
        {
            // Arrange
            var operario = Operario.Crear("Juan", "hash-pin");
            operario.Desactivar(Motivo, AdminId, Ahora);

            // Act
            var cambio = operario.Reactivar(MotivoDeCambio.Create("Volvió a trabajar"), AdminId, Ahora);

            // Assert
            Assert.True(operario.Activo);
            Assert.Equal("hash-pin", operario.PinHash);
            Assert.Equal("Inactivo", cambio.ValorAnterior);
            Assert.Equal("Activo", cambio.ValorNuevo);
            Assert.Equal("Volvió a trabajar", cambio.Motivo);
        }

        [Fact]
        public void Reactivar_AlreadyActive_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var operario = Operario.Crear("Juan", "hash-pin");

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => operario.Reactivar(Motivo, AdminId, Ahora));

            // Assert
            Assert.Equal("id", ex.Campo);
        }

        [Fact]
        public void RegistrarAlta_NewOperario_WritesTheCreationEntry()
        {
            // Arrange
            var operario = Operario.Crear("Juan", "hash-pin");

            // Act
            var cambio = operario.RegistrarAlta(AdminId, Ahora);

            // Assert
            Assert.Equal(CambioRegistrado.EntidadOperario, cambio.Entidad);
            Assert.Equal(CambioRegistrado.Campos.Alta, cambio.Campo);
            Assert.Equal("Juan", cambio.ValorNuevo);
            Assert.Equal(AdminId, cambio.UsuarioId);
        }

        [Fact]
        public void CambiarPin_NewHash_ReplacesTheHashAndTheEntryNeverHoldsIt()
        {
            // Arrange
            var operario = Operario.Crear("Juan", "hash-1");

            // Act
            var cambio = operario.CambiarPin("hash-2", AdminId, Ahora);

            // Assert
            Assert.Equal("hash-2", operario.PinHash);
            Assert.Equal(CambioRegistrado.Campos.Pin, cambio.Campo);
            Assert.Null(cambio.ValorAnterior);
            Assert.DoesNotContain("hash", cambio.ValorNuevo);
        }

        [Fact]
        public void CambiarPin_EmptyHash_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var operario = Operario.Crear("Juan", "hash-1");

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => operario.CambiarPin(" ", AdminId, Ahora));

            // Assert
            Assert.Equal("pin", ex.Campo);
        }
    }
}
