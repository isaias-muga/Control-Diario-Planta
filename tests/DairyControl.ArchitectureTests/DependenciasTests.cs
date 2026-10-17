using System.Reflection;
using DairyControl.Domain.Exceptions;
using NetArchTest.Rules;

namespace DairyControl.ArchitectureTests
{
    // The dependency rules of CLAUDE.md. A failure lists the offending types.
    public class DependenciasTests
    {
        private static string Detalle(NetArchTest.Rules.TestResult resultado) =>
            "Offending types: " + string.Join(", ", resultado.FailingTypeNames ?? []);

        // The BCL is what ships with the runtime: the assembly is loaded from the runtime's own folder. A NuGet package
        // called System.Something (Jwt, Pkcs, Data.SqlClient...) is loaded from the build output instead.
        private static readonly string CarpetaDelRuntime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        private static bool EsBcl(AssemblyName referencia)
        {
            var ensamblado = Assembly.Load(referencia);
            return string.Equals(Path.GetDirectoryName(ensamblado.Location), CarpetaDelRuntime, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Domain_ReferencesOnlyTheBcl()
        {
            // Arrange
            var referencias = Proyecto.Domain.GetReferencedAssemblies();

            // Act
            var ajenas = referencias.Where(r => !EsBcl(r)).Select(r => r.Name).ToList();

            // Assert
            Assert.True(ajenas.Count == 0, "Domain references: " + string.Join(", ", ajenas));
        }

        [Fact]
        public void Application_DoesNotDependOnInfrastructureEfCoreOrAspNetCore()
        {
            // Arrange
            var tipos = Types.InAssembly(Proyecto.Application);

            // Act
            var resultado = tipos.ShouldNot().HaveDependencyOnAny(
                "DairyControl.Infrastructure",
                "DairyControl.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore").GetResult();

            // Assert
            Assert.True(resultado.IsSuccessful, Detalle(resultado));
        }

        [Fact]
        public void Application_DoesNotReferenceInfrastructureEfCoreOrAspNetCoreAssemblies()
        {
            // Arrange
            var referencias = Proyecto.Application.GetReferencedAssemblies();

            // Act
            var prohibidas = referencias
                .Select(r => r.Name!)
                .Where(n => n.StartsWith("DairyControl.Infrastructure", StringComparison.Ordinal)
                    || n.StartsWith("DairyControl.Api", StringComparison.Ordinal)
                    || n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                    || n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
                .ToList();

            // Assert
            Assert.True(prohibidas.Count == 0, "Application references: " + string.Join(", ", prohibidas));
        }

        [Fact]
        public void Infrastructure_DoesNotDependOnApi()
        {
            // Arrange
            var tipos = Types.InAssembly(Proyecto.Infrastructure);

            // Act
            var resultado = tipos.ShouldNot().HaveDependencyOn("DairyControl.Api").GetResult();

            // Assert
            Assert.True(resultado.IsSuccessful, Detalle(resultado));
        }

        [Fact]
        public void Controllers_DoNotUseTheDbContextOrTheRepositories()
        {
            // Arrange: controllers talk to application services only; the repositories are the Domain interfaces
            var controladores = Types.InAssembly(Proyecto.Api).That().HaveNameEndingWith("Controller");

            // Act
            var resultado = controladores.ShouldNot().HaveDependencyOnAny(
                "DairyControl.Infrastructure",
                "DairyControl.Domain.Interfaces",
                "Microsoft.EntityFrameworkCore").GetResult();

            // Assert
            Assert.True(resultado.IsSuccessful, Detalle(resultado));
        }

        [Fact]
        public void Controllers_ExistSoTheRuleIsNotEmpty()
        {
            // Arrange / Act
            var controladores = Types.InAssembly(Proyecto.Api).That().HaveNameEndingWith("Controller").GetTypes().ToList();

            // Assert
            Assert.True(controladores.Count >= 4, "Expected the API controllers, found " + controladores.Count);
        }

        [Fact]
        public void DomainExceptions_AllDeriveFromExcepcionDeDominioAndLiveInTheExceptionsNamespace()
        {
            // Arrange
            var excepciones = Proyecto.Domain.GetTypes()
                .Where(t => typeof(Exception).IsAssignableFrom(t))
                .ToList();

            // Act
            var mal = excepciones
                .Where(t => t != typeof(ExcepcionDeDominio))
                .Where(t => !typeof(ExcepcionDeDominio).IsAssignableFrom(t) || t.Namespace != Proyecto.NamespaceExcepciones)
                .Select(t => t.FullName)
                .ToList();

            // Assert
            Assert.Contains(typeof(ReglaDeNegocioException), excepciones);
            Assert.True(mal.Count == 0, "Domain exceptions that do not derive from ExcepcionDeDominio or are outside " + Proyecto.NamespaceExcepciones + ": " + string.Join(", ", mal));
        }
    }
}
