using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Domain.Tests
{
    // Corrections and voids of a recepción: the same checks as Registrar, one history entry per changed field, nothing without a reason.
    public class RecepcionLecheCorreccionTests
    {
        private static readonly Guid ProveedorId = Guid.NewGuid();
        private static readonly DateTime Ahora = new(2026, 10, 15, 12, 0, 0);
        private static readonly DateTime Fecha = new(2026, 10, 1, 6, 0, 0);
        private static readonly Guid UsuarioId = Guid.NewGuid();
        private static readonly Guid AdminId = Guid.NewGuid();
        private static readonly DateTime AhoraUtc = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);
        private static readonly MotivoDeCambio Motivo = MotivoDeCambio.Create("Error de tipeo en la balanza");

        private static RecepcionLeche Registrada(int? ticket = 4980)
        {
            var parametros = ParametrosCalidad.Create(3.5m, 18m, 4m, 1000m, 1m);
            return RecepcionLeche.Registrar(ProveedorId, parametros, Fecha, Ahora, 1, "Primera carga", UsuarioId, null, AhoraUtc, ticket);
        }

        private static IReadOnlyList<CambioRegistrado> Corregir(RecepcionLeche r, Guid? proveedorId = null, decimal litros = 1000m, decimal? grasa = 3.5m, decimal acidez = 18m,
            decimal temperatura = 4m, decimal? agua = 1m, DateTime? fecha = null, int? silo = 1, string? observaciones = "Primera carga", int? ticket = 4980, MotivoDeCambio? motivo = null)
        {
            var parametros = ParametrosCalidad.Create(grasa, acidez, temperatura, litros, agua);
            return r.Corregir(proveedorId ?? ProveedorId, "Tambo Viejo", "Tambo Nuevo", parametros, fecha ?? Fecha, Ahora, silo, observaciones, ticket, motivo ?? Motivo, AdminId, AhoraUtc);
        }

        [Fact]
        public void Corregir_ChangeLitros_UpdatesTheRecepcionAndRecordsOldAndNewWithReasonUserAndTime()
        {
            // Arrange
            var recepcion = Registrada();

            // Act
            var cambios = Corregir(recepcion, litros: 1250.5m);

            // Assert
            Assert.Equal(1250.5m, recepcion.Parametros.Litros);
            var cambio = Assert.Single(cambios);
            Assert.Equal(CambioRegistrado.Campos.Litros, cambio.Campo);
            Assert.Equal("1000", cambio.ValorAnterior);
            Assert.Equal("1250.5", cambio.ValorNuevo);
            Assert.Equal("Error de tipeo en la balanza", cambio.Motivo);
            Assert.Equal(AdminId, cambio.UsuarioId);
            Assert.Equal(AhoraUtc, cambio.EnUtc);
            Assert.Equal(CambioRegistrado.EntidadRecepcion, cambio.Entidad);
            Assert.Equal(recepcion.Id, cambio.EntidadId);
        }

        [Fact]
        public void Corregir_SeveralFields_RecordsOneEntryPerFieldSharingTheCorrectionId()
        {
            // Arrange
            var recepcion = Registrada();

            // Act
            var cambios = Corregir(recepcion, litros: 900m, acidez: 17m, silo: 2, observaciones: null);

            // Assert
            Assert.Equal(["litros", "acidez", "silo", "observaciones"], cambios.Select(c => c.Campo).ToArray());
            Assert.Single(cambios.Select(c => c.CorreccionId).Distinct());
            Assert.Equal(2, recepcion.Silo);
            Assert.Null(recepcion.Observaciones);
            Assert.Equal("Primera carga", cambios.Single(c => c.Campo == "observaciones").ValorAnterior);
            Assert.Null(cambios.Single(c => c.Campo == "observaciones").ValorNuevo);
        }

        [Fact]
        public void Corregir_ChangeProveedor_RecordsTheNamesNotTheIds()
        {
            // Arrange
            var recepcion = Registrada();
            var otro = Guid.NewGuid();

            // Act
            var cambios = Corregir(recepcion, proveedorId: otro);

            // Assert
            Assert.Equal(otro, recepcion.ProveedorId);
            var cambio = Assert.Single(cambios);
            Assert.Equal(CambioRegistrado.Campos.Proveedor, cambio.Campo);
            Assert.Equal("Tambo Viejo", cambio.ValorAnterior);
            Assert.Equal("Tambo Nuevo", cambio.ValorNuevo);
        }

        [Fact]
        public void Corregir_ChangeTicketAndDate_RecordsInvariantText()
        {
            // Arrange
            var recepcion = Registrada();

            // Act
            var cambios = Corregir(recepcion, ticket: 5001, fecha: new DateTime(2026, 10, 2, 7, 30, 0));

            // Assert
            Assert.Equal("4980", cambios.Single(c => c.Campo == "ticket").ValorAnterior);
            Assert.Equal("5001", cambios.Single(c => c.Campo == "ticket").ValorNuevo);
            Assert.Equal("2026-10-01T06:00:00", cambios.Single(c => c.Campo == "fechaHora").ValorAnterior);
            Assert.Equal("2026-10-02T07:30:00", cambios.Single(c => c.Campo == "fechaHora").ValorNuevo);
        }

        [Fact]
        public void Corregir_NothingChanges_ThrowsAndLeavesTheRecepcionAsItWas()
        {
            // Arrange
            var recepcion = Registrada();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Corregir(recepcion));

            // Assert
            Assert.Equal("cambios", ex.Campo);
            Assert.Equal(1000m, recepcion.Parametros.Litros);
        }

        [Fact]
        public void Corregir_SameTextWithBlanks_CountsAsNoChange()
        {
            // Arrange
            var recepcion = Registrada();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Corregir(recepcion, observaciones: "  Primera carga  "));

            // Assert
            Assert.Equal("cambios", ex.Campo);
        }

        [Theory]
        [InlineData(0.0005)]
        [InlineData(30001)]
        public void Corregir_ImplausibleLitros_IsRejectedLikeWhenRegistering(double litros)
        {
            // Arrange & Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => ParametrosCalidad.Create(null, 18m, 4m, (decimal)litros));

            // Assert: the correction takes the same ParametrosCalidad, so the same ranges apply
            Assert.Equal("litros", ex.Campo);
        }

        [Fact]
        public void Corregir_SiloOutOfPlausibleRange_Throws()
        {
            // Arrange
            var recepcion = Registrada();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Corregir(recepcion, silo: 21));

            // Assert
            Assert.Equal("silo", ex.Campo);
        }

        [Fact]
        public void Corregir_NoSilo_Throws()
        {
            // Arrange
            var recepcion = Registrada();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Corregir(recepcion, silo: null));

            // Assert
            Assert.Equal("silo", ex.Campo);
        }

        [Fact]
        public void Corregir_DateTooFarAhead_Throws()
        {
            // Arrange
            var recepcion = Registrada();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Corregir(recepcion, fecha: Ahora.AddHours(2)));

            // Assert
            Assert.Equal("fechaHora", ex.Campo);
        }

        [Fact]
        public void Corregir_DateBefore2000_Throws()
        {
            // Arrange
            var recepcion = Registrada();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Corregir(recepcion, fecha: new DateTime(1999, 12, 31)));

            // Assert
            Assert.Equal("fechaHora", ex.Campo);
        }

        [Fact]
        public void Corregir_ObservacionesTooLong_Throws()
        {
            // Arrange
            var recepcion = Registrada();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Corregir(recepcion, observaciones: new string('x', RecepcionLeche.ObservacionesMaxLength + 1)));

            // Assert
            Assert.Equal("observaciones", ex.Campo);
        }

        [Fact]
        public void Corregir_NonPositiveTicket_Throws()
        {
            // Arrange
            var recepcion = Registrada();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Corregir(recepcion, ticket: 0));

            // Assert
            Assert.Equal("ticket", ex.Campo);
        }

        [Fact]
        public void Corregir_RemovingAnExistingTicket_ThrowsBecauseItStaysUsed()
        {
            // Arrange
            var recepcion = Registrada(ticket: 4980);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Corregir(recepcion, ticket: null));

            // Assert
            Assert.Equal("ticket", ex.Campo);
            Assert.Equal(4980, recepcion.Ticket);
        }

        [Fact]
        public void Corregir_AddingATicketToARecepcionWithoutOne_IsAllowed()
        {
            // Arrange
            var recepcion = Registrada(ticket: null);

            // Act
            var cambios = Corregir(recepcion, ticket: 7000);

            // Assert
            Assert.Equal(7000, recepcion.Ticket);
            Assert.Null(cambios.Single().ValorAnterior);
        }

        [Fact]
        public void Corregir_VoidedRecepcion_Throws()
        {
            // Arrange
            var recepcion = Registrada();
            recepcion.Anular(Motivo, AdminId, AhoraUtc);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Corregir(recepcion, litros: 1100m));

            // Assert
            Assert.Contains("anulada", ex.Message);
        }

        [Fact]
        public void Corregir_EmptyUser_Throws()
        {
            // Arrange
            var recepcion = Registrada();
            var parametros = ParametrosCalidad.Create(3.5m, 18m, 4m, 1100m, 1m);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() =>
                recepcion.Corregir(ProveedorId, "a", "a", parametros, Fecha, Ahora, 1, "Primera carga", 4980, Motivo, Guid.Empty, AhoraUtc));

            // Assert
            Assert.Equal("usuarioId", ex.Campo);
        }

        [Fact]
        public void Anular_ValidReason_MarksItVoidedWithWhoWhenAndWhyAndRecordsTheEntry()
        {
            // Arrange
            var recepcion = Registrada();

            // Act
            var cambio = recepcion.Anular(Motivo, AdminId, AhoraUtc);

            // Assert
            Assert.True(recepcion.EstaAnulada);
            Assert.Equal(AhoraUtc, recepcion.AnuladaEn);
            Assert.Equal(AdminId, recepcion.AnuladaPorUsuarioId);
            Assert.Equal("Error de tipeo en la balanza", recepcion.MotivoAnulacion);
            Assert.Equal(4980, recepcion.Ticket); // the ticket stays on the record: it stays used
            Assert.Equal(CambioRegistrado.Campos.Anulacion, cambio.Campo);
            Assert.Equal("Vigente", cambio.ValorAnterior);
            Assert.Equal("Anulada", cambio.ValorNuevo);
        }

        [Fact]
        public void Anular_AlreadyVoided_Throws()
        {
            // Arrange
            var recepcion = Registrada();
            recepcion.Anular(Motivo, AdminId, AhoraUtc);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => recepcion.Anular(Motivo, AdminId, AhoraUtc));

            // Assert
            Assert.Contains("ya está anulada", ex.Message);
        }

        [Fact]
        public void Vigente_Expression_IsTrueOnlyForRecepcionesThatAreNotVoided()
        {
            // Arrange
            var vigente = RecepcionLeche.Vigente.Compile();
            var valida = Registrada(ticket: 1);
            var anulada = Registrada(ticket: 2);
            anulada.Anular(Motivo, AdminId, AhoraUtc);

            // Act & Assert
            Assert.True(vigente(valida));
            Assert.False(vigente(anulada));
        }
    }
}
