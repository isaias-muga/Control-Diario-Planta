using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;

namespace DairyControl.Domain.Tests
{
    public class ProveedorTests
    {
        [Fact]
        public void Crear_ValidData_StoresTrimmedNameAndCreator()
        {
            // Arrange
            var usuarioId = Guid.NewGuid();

            // Act
            var proveedor = Proveedor.Crear("  Tambo Don Pedro ", usuarioId);

            // Assert
            Assert.Equal("Tambo Don Pedro", proveedor.Nombre);
            Assert.Equal(usuarioId, proveedor.CreadoPorUsuarioId);
        }

        [Fact]
        public void Crear_EmptyCreator_ThrowsReglaDeNegocioException()
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Proveedor.Crear("Tambo", Guid.Empty));

            // Assert
            Assert.Equal("creadoPorUsuarioId", ex.Campo);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Crear_BlankName_ThrowsReglaDeNegocioException(string nombre)
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Proveedor.Crear(nombre, Guid.NewGuid()));

            // Assert
            Assert.Equal("nombre", ex.Campo);
        }
    }
}
