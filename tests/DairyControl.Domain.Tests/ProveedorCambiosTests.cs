using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Domain.Tests
{
    // Rename, deactivate and reactivate a proveedor: each needs a reason and leaves a history entry; nothing is deleted.
    public class ProveedorCambiosTests
    {
        private static readonly Guid AdminId = Guid.NewGuid();
        private static readonly DateTime AhoraUtc = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);
        private static readonly MotivoDeCambio Motivo = MotivoDeCambio.Create("El nombre estaba mal escrito");

        private static Proveedor Nuevo() => Proveedor.Crear("Tambo Don Pedro", Guid.NewGuid());

        [Fact]
        public void Crear_NewProveedor_IsActive()
        {
            // Arrange & Act
            var proveedor = Nuevo();

            // Assert
            Assert.True(proveedor.Activo);
        }

        [Fact]
        public void Renombrar_NewName_ChangesItAndRecordsOldAndNew()
        {
            // Arrange
            var proveedor = Nuevo();

            // Act
            var cambio = proveedor.Renombrar("  Tambo Don Pedro S.A.  ", Motivo, AdminId, AhoraUtc);

            // Assert
            Assert.Equal("Tambo Don Pedro S.A.", proveedor.Nombre);
            Assert.Equal(CambioRegistrado.EntidadProveedor, cambio.Entidad);
            Assert.Equal(proveedor.Id, cambio.EntidadId);
            Assert.Equal(CambioRegistrado.Campos.Nombre, cambio.Campo);
            Assert.Equal("Tambo Don Pedro", cambio.ValorAnterior);
            Assert.Equal("Tambo Don Pedro S.A.", cambio.ValorNuevo);
            Assert.Equal("El nombre estaba mal escrito", cambio.Motivo);
            Assert.Equal(AdminId, cambio.UsuarioId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Renombrar_BlankName_ThrowsAndKeepsTheName(string nombre)
        {
            // Arrange
            var proveedor = Nuevo();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => proveedor.Renombrar(nombre, Motivo, AdminId, AhoraUtc));

            // Assert
            Assert.Equal("nombre", ex.Campo);
            Assert.Equal("Tambo Don Pedro", proveedor.Nombre);
        }

        [Fact]
        public void Renombrar_NameTooLong_Throws()
        {
            // Arrange
            var proveedor = Nuevo();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => proveedor.Renombrar(new string('x', Proveedor.NombreMaxLength + 1), Motivo, AdminId, AhoraUtc));

            // Assert
            Assert.Equal("nombre", ex.Campo);
        }

        [Fact]
        public void Renombrar_SameName_Throws()
        {
            // Arrange
            var proveedor = Nuevo();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => proveedor.Renombrar(" Tambo Don Pedro ", Motivo, AdminId, AhoraUtc));

            // Assert
            Assert.Equal("nombre", ex.Campo);
        }

        [Fact]
        public void Desactivar_ActiveProveedor_BecomesInactiveAndRecordsIt()
        {
            // Arrange
            var proveedor = Nuevo();

            // Act
            var cambio = proveedor.Desactivar(Motivo, AdminId, AhoraUtc);

            // Assert
            Assert.False(proveedor.Activo);
            Assert.Equal(CambioRegistrado.Campos.Activo, cambio.Campo);
            Assert.Equal("Activo", cambio.ValorAnterior);
            Assert.Equal("Inactivo", cambio.ValorNuevo);
        }

        [Fact]
        public void Desactivar_AlreadyInactive_Throws()
        {
            // Arrange
            var proveedor = Nuevo();
            proveedor.Desactivar(Motivo, AdminId, AhoraUtc);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => proveedor.Desactivar(Motivo, AdminId, AhoraUtc));

            // Assert
            Assert.Contains("ya está desactivado", ex.Message);
        }

        [Fact]
        public void Reactivar_InactiveProveedor_BecomesActiveAndRecordsIt()
        {
            // Arrange
            var proveedor = Nuevo();
            proveedor.Desactivar(Motivo, AdminId, AhoraUtc);

            // Act
            var cambio = proveedor.Reactivar(Motivo, AdminId, AhoraUtc);

            // Assert
            Assert.True(proveedor.Activo);
            Assert.Equal("Inactivo", cambio.ValorAnterior);
            Assert.Equal("Activo", cambio.ValorNuevo);
        }

        [Fact]
        public void Reactivar_AlreadyActive_Throws()
        {
            // Arrange
            var proveedor = Nuevo();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => proveedor.Reactivar(Motivo, AdminId, AhoraUtc));

            // Assert
            Assert.Contains("ya está activo", ex.Message);
        }
    }
}
