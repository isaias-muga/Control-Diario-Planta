using System.Text.RegularExpressions;

namespace DairyControl.ArchitectureTests
{
    /// <summary>
    /// This repository can deploy only the production instance, only by hand. The demo is deployed from another repository and a merge to
    /// main must never deploy anything.
    /// </summary>
    public class WorkflowsTests
    {
        private const string Produccion = "deploy-production.yml";

        private static readonly string[] SecretosPermitidos = ["AZURE_CLIENT_ID", "AZURE_TENANT_ID", "AZURE_SUBSCRIPTION_ID"];

        private static string CarpetaDeWorkflows => Path.Combine(Proyecto.Raiz, ".github", "workflows");

        private static IEnumerable<string> Workflows() =>
            Directory.EnumerateFiles(CarpetaDeWorkflows, "*.y*ml");

        // Lines of a workflow without comments
        private static string SinComentarios(string ruta) =>
            string.Join('\n', File.ReadAllLines(ruta).Where(l => !l.TrimStart().StartsWith('#')));

        [Fact]
        public void DeployProduction_Triggers_AreOnlyWorkflowDispatch()
        {
            // Arrange
            var texto = SinComentarios(Path.Combine(CarpetaDeWorkflows, Produccion));

            // Act: the triggers are the keys one level under "on:"
            var disparadores = Regex.Matches(texto, @"(?m)^on:\s*\n((?:[ \t]+.*\n?)+)")
                .Select(m => m.Groups[1].Value)
                .SelectMany(bloque => Regex.Matches(bloque, @"(?m)^  (\w+):").Select(m => m.Groups[1].Value))
                .ToList();

            // Assert
            Assert.Equal(["workflow_dispatch"], disparadores);
        }

        [Fact]
        public void DeployProduction_Secrets_AreOnlyTheAzureIdentityOnes()
        {
            // Arrange
            var texto = SinComentarios(Path.Combine(CarpetaDeWorkflows, Produccion));

            // Act
            var usados = Regex.Matches(texto, @"secrets\.(\w+)").Select(m => m.Groups[1].Value).Distinct().ToList();

            // Assert
            Assert.All(usados, s => Assert.Contains(s, SecretosPermitidos));
            Assert.DoesNotContain("password", texto, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DeployProduction_Job_RunsOnlyFromMain()
        {
            // Arrange
            var texto = SinComentarios(Path.Combine(CarpetaDeWorkflows, Produccion));

            // Assert
            Assert.Contains("github.ref == 'refs/heads/main'", texto);
        }

        [Fact]
        public void OtherWorkflows_CannotDeploy()
        {
            // Arrange: what it takes to publish an image or to touch Azure
            string[] capacidadesDeDespliegue =
            [
                "azure/login", "az containerapp", "az acr", "az login", "docker push", "ghcr.io", "packages: write", "id-token: write",
            ];

            // Act
            var otros = Workflows().Where(w => !string.Equals(Path.GetFileName(w), Produccion, StringComparison.OrdinalIgnoreCase)).ToList();
            var infracciones = otros
                .SelectMany(w => capacidadesDeDespliegue
                    .Where(c => SinComentarios(w).Contains(c, StringComparison.OrdinalIgnoreCase))
                    .Select(c => $"{Path.GetFileName(w)}: {c}"))
                .ToList();

            // Assert
            Assert.NotEmpty(otros); // ci.yml exists: the scan is looking at something
            Assert.Empty(infracciones);
        }

        [Fact]
        public void Ci_RequiredChecks_AlwaysStartAndReport()
        {
            // Arrange: the ruleset requires these three contexts; a required check that is not reported blocks the PR forever
            var texto = SinComentarios(Path.Combine(CarpetaDeWorkflows, "ci.yml"));

            // Assert: the jobs exist, nothing filters or skips them at workflow or job level, and no trigger is label- or comment-based
            foreach (var job in new[] { "build-test", "client", "e2e" })
                Assert.Matches($@"(?m)^  {job}:\s*$", texto);
            Assert.DoesNotMatch(@"(?m)^\s*paths(-ignore)?:", texto);
            Assert.DoesNotMatch(@"(?m)^  (build-test|client|e2e):[ 	]*?
(?:    (?!steps:)[^
]*
)*?    if:", texto);
            foreach (var disparador in new[] { "labeled", "issue_comment", "workflow_run" })
                Assert.DoesNotContain(disparador, texto);
        }

        [Fact]
        public void Ci_PlaywrightImage_MatchesTheVersionOfTheLockfile()
        {
            // Arrange: the e2e tests run in the official Playwright image, whose browsers must match @playwright/test
            var texto = SinComentarios(Path.Combine(CarpetaDeWorkflows, "ci.yml"));
            var bloqueo = File.ReadAllText(Path.Combine(Proyecto.Raiz, "e2e", "package-lock.json"));

            // Act
            var enImagen = Regex.Match(texto, @"mcr\.microsoft\.com/playwright:v([\d.]+)-").Groups[1].Value;
            var enBloqueo = Regex.Match(bloqueo, @"""node_modules/@playwright/test"":\s*\{\s*""version"":\s*""([\d.]+)""").Groups[1].Value;

            // Assert
            Assert.NotEmpty(enImagen);
            Assert.Equal(enBloqueo, enImagen);
            Assert.DoesNotContain("apt-get", texto);
            Assert.DoesNotContain("--with-deps", texto);
        }

        [Fact]
        public void WorkflowsFolder_HasNoOtherDeployWorkflow()
        {
            // Act
            var despliegues = Workflows()
                .Select(Path.GetFileName)
                .Where(n => n!.Contains("deploy", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Assert
            Assert.Equal([Produccion], despliegues);
        }
    }
}
