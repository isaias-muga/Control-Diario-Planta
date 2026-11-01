using DairyControl.Kernel;
using DairyControl.Kernel.Auditing;
using DairyControl.Kernel.Exceptions;

namespace DairyControl.Kernel.Tests;

public class CambioRegistradoTests
{
    private static readonly DateTime Ahora = new(2026, 10, 23, 3, 0, 0, DateTimeKind.Utc);
    private static readonly MotivoDeCambio Motivo = MotivoDeCambio.Create("Error de carga");

    private static CambioRegistrado Registrar(string entidad = "Recepcion", Guid? entidadId = null, Guid? correccionId = null, string campo = "litros",
        string? anterior = "1", string? nuevo = "2", Guid? usuarioId = null, DateTime? enUtc = null) =>
        CambioRegistrado.Registrar(entidad, entidadId ?? Guid.NewGuid(), correccionId ?? Guid.NewGuid(), campo, anterior, nuevo,
            usuarioId ?? Guid.NewGuid(), enUtc ?? Ahora, Motivo);

    [Fact]
    public void Registrar_DatosValidos_GuardaTodosLosCampos()
    {
        // Arrange
        var entidadId = Guid.NewGuid();
        var correccionId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();

        // Act
        var cambio = Registrar(entidadId: entidadId, correccionId: correccionId, usuarioId: usuarioId);

        // Assert
        Assert.NotEqual(Guid.Empty, cambio.Id);
        Assert.Equal("Recepcion", cambio.Entidad);
        Assert.Equal(entidadId, cambio.EntidadId);
        Assert.Equal(correccionId, cambio.CorreccionId);
        Assert.Equal("litros", cambio.Campo);
        Assert.Equal("1", cambio.ValorAnterior);
        Assert.Equal("2", cambio.ValorNuevo);
        Assert.Equal(usuarioId, cambio.UsuarioId);
        Assert.Equal(Ahora, cambio.EnUtc);
        Assert.Equal("Error de carga", cambio.Motivo);
    }

    [Fact]
    public void Registrar_EntidadIdVacio_LanzaReglaDeNegocio()
    {
        // Arrange
        // Act
        var ex = Assert.Throws<ReglaDeNegocioException>(() => Registrar(entidadId: Guid.Empty));

        // Assert
        Assert.Equal("entidadId", ex.Campo);
        Assert.Equal("El cambio debe indicar qué registro modifica.", ex.Message);
    }

    [Fact]
    public void Registrar_CorreccionIdVacio_LanzaReglaDeNegocio()
    {
        // Arrange
        // Act
        var ex = Assert.Throws<ReglaDeNegocioException>(() => Registrar(correccionId: Guid.Empty));

        // Assert
        Assert.Equal("entidadId", ex.Campo);
    }

    [Fact]
    public void Registrar_UsuarioIdVacio_LanzaReglaDeNegocio()
    {
        // Arrange
        // Act
        var ex = Assert.Throws<ReglaDeNegocioException>(() => Registrar(usuarioId: Guid.Empty));

        // Assert
        Assert.Equal("usuarioId", ex.Campo);
        Assert.Equal("El usuario que hace el cambio es obligatorio.", ex.Message);
    }

    [Fact]
    public void Registrar_CampoDe51Caracteres_LanzaReglaDeNegocio()
    {
        // Arrange
        // Act
        var ex = Assert.Throws<ReglaDeNegocioException>(() => Registrar(campo: new string('c', 51)));

        // Assert
        Assert.Equal("campo", ex.Campo);
        Assert.Equal("El campo del cambio no es válido.", ex.Message);
    }

    [Fact]
    public void Registrar_FechaPorDefecto_LanzaReglaDeNegocio()
    {
        // Arrange
        // Act
        var ex = Assert.Throws<ReglaDeNegocioException>(() => Registrar(enUtc: default(DateTime)));

        // Assert
        Assert.Equal("enUtc", ex.Campo);
        Assert.Equal("La fecha del cambio es obligatoria.", ex.Message);
    }

    [Fact]
    public void Registrar_ValorDe501Caracteres_LoRecortaA500()
    {
        // Arrange
        var largo = new string('x', 501);

        // Act
        var cambio = Registrar(anterior: largo, nuevo: largo);

        // Assert
        Assert.Equal(500, cambio.ValorAnterior!.Length);
        Assert.Equal(500, cambio.ValorNuevo!.Length);
    }

    [Fact]
    public void Registrar_EntidadDe20Caracteres_CreaElCambio()
    {
        // Arrange
        // Act
        var cambio = Registrar(entidad: new string('a', 20));

        // Assert
        Assert.Equal(20, cambio.Entidad.Length);
    }

    [Fact]
    public void Registrar_EntidadDe21Caracteres_LanzaReglaDeNegocio()
    {
        // Arrange
        // Act
        var ex = Assert.Throws<ReglaDeNegocioException>(() => Registrar(entidad: new string('a', 21)));

        // Assert
        Assert.Equal("entidad", ex.Campo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Registrar_EntidadVaciaOEnBlanco_LanzaReglaDeNegocio(string entidad)
    {
        // Arrange
        // Act
        var ex = Assert.Throws<ReglaDeNegocioException>(() => Registrar(entidad: entidad));

        // Assert
        Assert.Equal("entidad", ex.Campo);
    }

    [Fact]
    public void Registrar_EntidadQueElKernelNoConoce_CreaElCambio()
    {
        // Arrange
        // Act
        var cambio = Registrar(entidad: "Produccion");

        // Assert
        Assert.Equal("Produccion", cambio.Entidad);
    }

    [Fact]
    public void CambioRegistrado_Propiedades_NoTienenSetterPublico()
    {
        // Arrange
        var propiedades = typeof(CambioRegistrado).GetProperties();

        // Act
        var conSetterPublico = propiedades.Where(p => p.SetMethod is { IsPublic: true }).Select(p => p.Name).ToList();

        // Assert
        Assert.NotEmpty(propiedades);
        Assert.Empty(conSetterPublico);
    }
}
