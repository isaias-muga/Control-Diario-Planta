using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using Moq;

namespace DairyControl.Api.Tests
{
    // The office clerk records the plant lab measurement: she registers a recepción with her own user, no PIN, and the silo must exist.
    public class RegistroPorOficinaTests : IClassFixture<FabricaDeApi>
    {
        private readonly FabricaDeApi _fabrica;

        public RegistroPorOficinaTests(FabricaDeApi fabrica)
        {
            _fabrica = fabrica;
        }

        private (HttpClient Cliente, Usuario Usuario, string Ruta) Preparar()
        {
            var usuario = _fabrica.CrearUsuario(RolUsuario.Oficina);
            var proveedorId = Guid.NewGuid();
            _fabrica.Proveedores.Setup(p => p.GetByIdAsync(proveedorId)).ReturnsAsync(Proveedor.Crear("Tambo Don Pedro", Guid.NewGuid()));
            return (_fabrica.ClienteDe(usuario), usuario, $"/api/Proveedores/{proveedorId}/recepciones");
        }

        private static object Cuerpo(int? silo) => new
        {
            acidez = 17,
            temperatura = 5,
            litros = 1000,
            silo,
            fechaHora = DairyControl.Application.HoraPlanta.Ahora(TimeProvider.System).AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm:ss"),
            ticket = 7000 + Random.Shared.Next(1, 1000)
        };

        [Fact]
        public async Task Oficina_RegistersWithAValidSilo_WithoutPin_AndHerUserIsStamped()
        {
            // Arrange
            var (cliente, usuario, ruta) = Preparar();
            RecepcionLeche? guardada = null;
            _fabrica.Recepciones.Setup(r => r.AddAsync(It.IsAny<RecepcionLeche>()))
                .Callback<RecepcionLeche>(r => guardada = r).Returns(Task.CompletedTask);

            // Act
            var respuesta = await cliente.PostAsJsonAsync(ruta, Cuerpo(2), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.NotNull(guardada);
            Assert.Equal(2, guardada.Silo);
            Assert.Equal(usuario.Id, guardada.RegistradoPorUsuarioId);
            Assert.Null(guardada.OperarioId);
        }

        [Fact]
        public async Task Oficina_WithoutSilo_Gets400OnSiloAndNothingIsSaved()
        {
            // Arrange
            var (cliente, _, ruta) = Preparar();
            _fabrica.Recepciones.Invocations.Clear();

            // Act
            var respuesta = await cliente.PostAsJsonAsync(ruta, Cuerpo(null), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal("silo", cuerpo.GetProperty("field").GetString());
            Assert.Equal("El silo es obligatorio.", cuerpo.GetProperty("error").GetString());
            _fabrica.Recepciones.Verify(r => r.AddAsync(It.IsAny<RecepcionLeche>()), Times.Never);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(0)]
        [InlineData(25)]
        public async Task Oficina_WithASiloAboveTheCount_Gets400OnSiloWithTheCount(int silo)
        {
            // Arrange: the plant has three silos
            var (cliente, _, ruta) = Preparar();
            _fabrica.Recepciones.Invocations.Clear();

            // Act
            var respuesta = await cliente.PostAsJsonAsync(ruta, Cuerpo(silo), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal("silo", cuerpo.GetProperty("field").GetString());
            Assert.Equal("El silo debe estar entre 1 y 3.", cuerpo.GetProperty("error").GetString());
            _fabrica.Recepciones.Verify(r => r.AddAsync(It.IsAny<RecepcionLeche>()), Times.Never);
        }
    }
}
