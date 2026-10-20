using System.Reflection;
using System.Text.RegularExpressions;
using DairyControl.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;

namespace DairyControl.ArchitectureTests
{
    // The authorization rules of CLAUDE.md: permissions are decided on the server, per action, with a named policy.
    public class AutorizacionTests
    {
        private static List<Type> Controladores() =>
            Types.InAssembly(Proyecto.Api).That().HaveNameEndingWith("Controller").GetTypes().ToList();

        private static IEnumerable<MethodInfo> Acciones(Type controlador) =>
            controlador.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes().Any(a => a is HttpMethodAttribute));

        private static readonly HashSet<string> PoliticasConocidas = typeof(Politicas)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

        [Fact]
        public void Policies_AreDefined()
        {
            // Assert: the rule below would pass vacuously without them
            Assert.True(PoliticasConocidas.Count >= 6, "Expected the policy names of Politicas, found " + PoliticasConocidas.Count);
        }

        [Fact]
        public void NoAction_IsAnHttpDelete_DataIsCorrectedVoidedOrDeactivatedNeverDeleted()
        {
            // Arrange
            var acciones = Controladores().SelectMany(c => Acciones(c).Select(m => (Controlador: c, Accion: m))).ToList();

            // Act: a controller action that answers DELETE would be a way to remove a recepción, a proveedor or a user for good
            var deletes = acciones
                .Where(x => x.Accion.GetCustomAttributes().OfType<HttpMethodAttribute>().Any(a => a.HttpMethods.Contains("DELETE", StringComparer.OrdinalIgnoreCase)))
                .Select(x => $"{x.Controlador.Name}.{x.Accion.Name}")
                .ToList();

            // Assert
            Assert.True(acciones.Count >= 15, "Expected the controller actions, found " + acciones.Count);
            Assert.True(deletes.Count == 0, "No DELETE endpoints (correct, void or deactivate instead): " + string.Join(", ", deletes));
        }

        [Fact]
        public void EveryAction_NamesAnExplicitPolicyOrIsAnonymous()
        {
            // Arrange
            var acciones = Controladores().SelectMany(c => Acciones(c).Select(m => (Controlador: c, Accion: m))).ToList();

            // Act: [Authorize(Policy = "...")] with a policy that exists, or [AllowAnonymous]
            var sinPolitica = acciones.Where(x =>
            {
                if (x.Accion.IsDefined(typeof(AllowAnonymousAttribute), false))
                {
                    return false;
                }
                var autorizaciones = x.Accion.GetCustomAttributes<AuthorizeAttribute>(false).ToList();
                return autorizaciones.Count != 1 || autorizaciones[0].Policy is null || !PoliticasConocidas.Contains(autorizaciones[0].Policy!);
            }).Select(x => $"{x.Controlador.Name}.{x.Accion.Name}").ToList();

            // Assert
            Assert.True(acciones.Count >= 15, "Expected the API actions, found " + acciones.Count);
            Assert.True(sinPolitica.Count == 0, "Actions without an explicit policy: " + string.Join(", ", sinPolitica));
        }

        [Fact]
        public void Controllers_HaveNoClassLevelAuthorize()
        {
            // Act: a class-level [Authorize] would be combined (AND) with the policy of each action and silently narrow it
            var conAutorizacionDeClase = Controladores()
                .Where(c => c.IsDefined(typeof(AuthorizeAttribute), true) || c.IsDefined(typeof(AllowAnonymousAttribute), true))
                .Select(c => c.Name)
                .ToList();

            // Assert
            Assert.True(conAutorizacionDeClase.Count == 0, "Controllers with a class-level attribute: " + string.Join(", ", conAutorizacionDeClase));
        }

        [Fact]
        public void OnlyTheLoginAction_IsAnonymousAmongTheControllers()
        {
            // Act
            var anonimas = Controladores()
                .SelectMany(c => Acciones(c).Where(m => m.IsDefined(typeof(AllowAnonymousAttribute), false)).Select(m => $"{c.Name}.{m.Name}"))
                .ToList();

            // Assert: the other anonymous endpoint is /health (next test)
            Assert.Equal(["AuthController.LoginAsync"], anonimas);
        }

        [Fact]
        public void OnlyTheHealthEndpoints_AreMappedAnonymouslyInTheSource()
        {
            // Act: every .AllowAnonymous() call in the API sources (comments skipped)
            var llamadas = Proyecto.Fuentes("DairyControl.Api")
                .SelectMany(ruta => File.ReadAllLines(ruta).Select(l => (Ruta: ruta, Linea: l.Trim())))
                .Where(x => !x.Linea.StartsWith("//") && !x.Linea.StartsWith("///") && Regex.IsMatch(x.Linea, @"\.AllowAnonymous\(\)"))
                .Select(x => Path.GetFileName(x.Ruta))
                .ToList();

            // Assert
            // /health and /health/live, both in the same file
            Assert.Equal(["HealthEndpoints.cs", "HealthEndpoints.cs"], llamadas);
        }
    }
}
