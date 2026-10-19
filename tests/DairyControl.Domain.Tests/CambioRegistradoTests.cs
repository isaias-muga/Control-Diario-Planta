using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Domain.Tests
{
    public class CambioRegistradoTests
    {
        private static readonly MotivoDeCambio Motivo = MotivoDeCambio.Create("Corrección de prueba");
        private static readonly DateTime EnUtc = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);

        private static CambioRegistrado Crear(string entidad = CambioRegistrado.EntidadRecepcion, Guid? entidadId = null, string campo = "litros", Guid? usuarioId = null,
            string? anterior = "1000", string? nuevo = "1100", DateTime? enUtc = null) =>
            CambioRegistrado.Crear(entidad, entidadId ?? Guid.NewGuid(), Guid.NewGuid(), campo, anterior, nuevo, usuarioId ?? Guid.NewGuid(), enUtc ?? EnUtc, Motivo);

        [Fact]
        public void Crear_ValidData_KeepsEveryValue()
        {
            // Arrange
            var usuarioId = Guid.NewGuid();

            // Act
            var cambio = Crear(usuarioId: usuarioId);

            // Assert
            Assert.Equal("litros", cambio.Campo);
            Assert.Equal("1000", cambio.ValorAnterior);
            Assert.Equal("1100", cambio.ValorNuevo);
            Assert.Equal(usuarioId, cambio.UsuarioId);
            Assert.Equal(EnUtc, cambio.EnUtc);
            Assert.Equal("Corrección de prueba", cambio.Motivo);
            Assert.NotEqual(Guid.Empty, cambio.Id);
        }

        [Fact]
        public void Crear_UnknownEntity_Throws()
        {
            // Arrange & Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Crear(entidad: "Usuario"));

            // Assert
            Assert.Equal("entidad", ex.Campo);
        }

        [Fact]
        public void Crear_EmptyUserOrEntityOrDate_Throws()
        {
            // Arrange, Act & Assert
            Assert.Equal("usuarioId", Assert.Throws<ReglaDeNegocioException>(() => Crear(usuarioId: Guid.Empty)).Campo);
            Assert.Equal("entidadId", Assert.Throws<ReglaDeNegocioException>(() => Crear(entidadId: Guid.Empty)).Campo);
            Assert.Equal("enUtc", Assert.Throws<ReglaDeNegocioException>(() => Crear(enUtc: default(DateTime))).Campo);
        }

        [Fact]
        public void Crear_BlankField_Throws()
        {
            // Arrange & Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Crear(campo: " "));

            // Assert
            Assert.Equal("campo", ex.Campo);
        }

        [Fact]
        public void Crear_VeryLongValue_IsCutToTheColumnSize()
        {
            // Arrange & Act
            var cambio = Crear(nuevo: new string('x', CambioRegistrado.ValorMaxLength + 50));

            // Assert
            Assert.Equal(CambioRegistrado.ValorMaxLength, cambio.ValorNuevo!.Length);
        }
    }
}
