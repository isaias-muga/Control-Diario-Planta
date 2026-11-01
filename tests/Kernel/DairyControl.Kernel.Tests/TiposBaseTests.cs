using System.Text.Json;
using DairyControl.Kernel.Authorization;
using DairyControl.Kernel.Contracts;
using DairyControl.Kernel.Exceptions;
using DairyControl.Kernel.TestSupport;

namespace DairyControl.Kernel.Tests;

public class TiposBaseTests
{
    // ---- K6 MotivoDeCambio ----
    [Fact]
    public void Create_TextoCortoConEspacios_LanzaReglaDeNegocioConCampoMotivo()
    {
        // Arrange
        // Act
        var ex = Assert.Throws<ReglaDeNegocioException>(() => MotivoDeCambio.Create("  abc  "));

        // Assert
        Assert.Equal("motivo", ex.Campo);
    }

    [Fact]
    public void Create_CincoCaracteres_CreaElMotivo()
    {
        // Arrange
        // Act
        var motivo = MotivoDeCambio.Create("abcde");

        // Assert
        Assert.Equal("abcde", motivo.Texto);
    }

    [Fact]
    public void Create_501Caracteres_LanzaReglaDeNegocioConCampoMotivo()
    {
        // Arrange
        // Act
        var ex = Assert.Throws<ReglaDeNegocioException>(() => MotivoDeCambio.Create(new string('m', 501)));

        // Assert
        Assert.Equal("motivo", ex.Campo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NuloOVacio_LanzaMotivoObligatorio(string? texto)
    {
        // Arrange
        // Act
        var ex = Assert.Throws<ReglaDeNegocioException>(() => MotivoDeCambio.Create(texto));

        // Assert
        Assert.Equal("El motivo es obligatorio.", ex.Message);
    }

    // ---- K7 exception categories ----
    private sealed class AccesoDenegadoFalso() : AccesoDenegadoDeDominioException("no");
    private sealed class ConflictoFalso() : ConflictoException("choque");

    [Fact]
    public void Excepciones_Categorias_DerivanDeLaCategoriaCorrecta()
    {
        // Arrange
        // Act
        // Assert
        Assert.IsAssignableFrom<ConflictoException>(new ConflictoDeConcurrenciaException());
        Assert.IsAssignableFrom<ConflictoException>(new ConflictoDeUnicidadException("m"));
        Assert.IsAssignableFrom<ExcepcionDeDominio>(new AccesoDenegadoFalso());
        Assert.IsAssignableFrom<ExcepcionDeDominio>(new ConflictoFalso());
        Assert.IsAssignableFrom<ExcepcionDeDominio>(new DemasiadosIntentosException("m"));
        Assert.Equal("c", new ReglaDeNegocioException("m", "c").Campo);
        Assert.Null(new ReglaDeNegocioException("m").Campo);
        Assert.Equal(ConflictoDeConcurrenciaException.MensajePredeterminado, new ConflictoDeConcurrenciaException().Message);
    }

    // ---- K8 the Kernel references nothing ----
    [Fact]
    public void Kernel_Ensamblado_SoloReferenciaLaBcl()
    {
        // Arrange
        var referencias = typeof(ContractVersionAttribute).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        // Act
        var ajenas = referencias.Where(n => !n.StartsWith("System", StringComparison.Ordinal) && n != "netstandard" && n != "mscorlib").ToList();

        // Assert
        Assert.NotEmpty(referencias);
        Assert.Empty(ajenas);
    }

    // ---- K9 HoraPlanta ----
    private static readonly RelojFijo Reloj = new(new DateTimeOffset(2026, 10, 23, 3, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Ahora_RelojFijoALas0300Utc_DevuelveMedianocheDeLaPlanta()
    {
        // Arrange
        // Act
        var ahora = HoraPlanta.Ahora(Reloj);

        // Assert
        Assert.Equal(new DateTime(2026, 10, 23, 0, 0, 0), ahora);
    }

    [Fact]
    public void AUtc_HoraDePlantaSinKind_DevuelveLas0300Utc()
    {
        // Arrange
        var local = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Unspecified);

        // Act
        var utc = HoraPlanta.AUtc(local);

        // Assert
        Assert.Equal(new DateTime(2026, 10, 23, 3, 0, 0), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void ALaPlanta_FechaUtc_LaConvierteAHoraDePlanta()
    {
        // Arrange
        var utc = new DateTime(2026, 10, 23, 3, 0, 0, DateTimeKind.Utc);

        // Act
        var planta = HoraPlanta.ALaPlanta(utc);

        // Assert
        Assert.Equal(new DateTime(2026, 10, 23, 0, 0, 0), planta);
    }

    [Fact]
    public void ALaPlanta_FechaSinKind_DevuelveElMismoValor()
    {
        // Arrange
        var local = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Unspecified);

        // Act
        var planta = HoraPlanta.ALaPlanta(local);

        // Assert
        Assert.Equal(local, planta);
    }

    // ---- K10 policies ----
    [Fact]
    public void Politicas_Constantes_TienenLosValoresDeHoy()
    {
        // Arrange
        // Act
        // Assert
        Assert.Equal("Lectura", Politicas.Lectura);
        Assert.Equal("LecturaCarga", Politicas.LecturaCarga);
        Assert.Equal("Registro", Politicas.Registro);
        Assert.Equal("Administracion", Politicas.Administracion);
        Assert.Equal("Autenticado", Politicas.Autenticado);
        Assert.Equal("CambioPassword", Politicas.CambioPassword);
    }

    // ---- K11 ApiErrors ----
    [Fact]
    public void Body_SoloError_SerializaSoloError()
    {
        // Arrange
        // Act
        var json = JsonSerializer.Serialize(ApiErrors.Body("x"));

        // Assert
        Assert.Equal("{\"error\":\"x\"}", json);
    }

    [Fact]
    public void Body_ConCampo_SerializaErrorYField()
    {
        // Arrange
        // Act
        var json = JsonSerializer.Serialize(ApiErrors.Body("x", "y"));

        // Assert
        Assert.Equal("{\"error\":\"x\",\"field\":\"y\"}", json);
    }

    // ---- K12 conventions ----
    [Fact]
    public void ApiConventions_Constantes_TienenLosValoresDeHoy()
    {
        // Arrange
        // Act
        // Assert
        Assert.Equal("login", ApiConventions.LoginRateLimitPolicy);
        Assert.Equal("debeCambiarPassword", ApiConventions.MustChangePasswordCode);
        Assert.Equal("sub", ApiConventions.Claims.Sub);
        Assert.Equal("role", ApiConventions.Claims.Rol);
        Assert.Equal("stamp", ApiConventions.Claims.Sello);
        Assert.Equal("name", ApiConventions.Claims.Nombre);
    }

    // ---- K13 consumer side: IUsuarioActual ----
    private sealed class ConsumidorDeUsuario(IUsuarioActual actual)
    {
        public string Describir() => actual.Id is null ? "anonimo" : $"{actual.Id}:{actual.Rol}";
    }

    [Fact]
    public void ConsumidorDeUsuario_ConUsuarioActualFijoAdmin_VeIdYRol()
    {
        // Arrange
        var id = Guid.NewGuid();
        var consumidor = new ConsumidorDeUsuario(new UsuarioActualFijo(id, RolUsuario.Admin));

        // Act
        var texto = consumidor.Describir();

        // Assert
        Assert.Equal($"{id}:Admin", texto);
    }

    [Fact]
    public void ConsumidorDeUsuario_ConUsuarioActualFijoNulo_EsAnonimo()
    {
        // Arrange
        var consumidor = new ConsumidorDeUsuario(new UsuarioActualFijo(null, null));

        // Act
        var texto = consumidor.Describir();

        // Assert
        Assert.Equal("anonimo", texto);
    }

    // ---- K14 RelojFijo ----
    [Fact]
    public void GetUtcNow_RelojFijo_DevuelveSiempreElMismoInstante()
    {
        // Arrange
        var esperado = new DateTimeOffset(2026, 10, 23, 3, 0, 0, TimeSpan.Zero);
        var reloj = new RelojFijo(esperado);

        // Act
        var primero = reloj.GetUtcNow();
        var segundo = reloj.GetUtcNow();

        // Assert
        Assert.Equal(esperado, primero);
        Assert.Equal(primero, segundo);
    }

    // ---- DTO shapes ----
    [Fact]
    public void CorreccionDto_Nuevo_TieneCambiosVaciosYUsuarioNulo()
    {
        // Arrange
        // Act
        var dto = new CorreccionDto();

        // Assert
        Assert.Empty(dto.Cambios);
        Assert.Null(dto.Usuario);
        Assert.Equal("", new MotivoCambioDto().Motivo);
    }
}
