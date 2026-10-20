using System.Text.RegularExpressions;

namespace DairyControl.ArchitectureTests
{
    // Rules about what the source says, checked by scanning the .cs files of src.
    public partial class FuentesTests
    {
        [GeneratedRegex(@"\bDateTime(Offset)?\s*\.\s*(Now|UtcNow|Today)\b")]
        private static partial Regex LecturaDeReloj();

        // new ArgumentException(...), new ArgumentOutOfRangeException(...) and ArgumentException/OutOfRange.ThrowIf...
        // (ArgumentNullException.ThrowIfNull stays allowed: a null argument is a bug)
        [GeneratedRegex(@"\b(new\s+Argument(OutOfRange)?Exception\b|Argument(OutOfRange)?Exception\s*\.\s*ThrowIf)")]
        private static partial Regex LanzaArgumentException();

        // Physical deletes: ExecuteDelete(Async), RemoveRange and a bare .Remove(...) on a DbSet or the context. Data is corrected, voided
        // or deactivated with a reason and a history, never deleted.
        [GeneratedRegex(@"\bExecuteDelete(Async)?\s*\(|\.RemoveRange\s*\(|\b_context\.\w+\.Remove\s*\(|\b_context\.Remove\s*\(")]
        private static partial Regex BorraFilas();

        // In Infrastructure (the only project that touches the database) any .Remove( is a delete, whatever the context field is called
        [GeneratedRegex(@"\.Remove\s*\(|\.Set<\w+>\(\)\s*\.Remove")]
        private static partial Regex RemueveEnInfraestructura();

        private static readonly string[] Proyectos =
        [
            "DairyControl.Domain",
            "DairyControl.Application",
            "DairyControl.Infrastructure",
            "DairyControl.Api",
        ];

        private static bool EsComentario(string linea)
        {
            var t = linea.TrimStart();
            return t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith('*') || t.StartsWith("/*", StringComparison.Ordinal);
        }

        private static List<string> Coincidencias(IEnumerable<string> proyectos, Regex patron) =>
            proyectos
                .SelectMany(Proyecto.Fuentes)
                .SelectMany(ruta => File.ReadLines(ruta).Select((linea, i) => (ruta, linea, numero: i + 1)))
                .Where(x => !EsComentario(x.linea) && patron.IsMatch(x.linea))
                .Select(x => $"{Path.GetRelativePath(Proyecto.Raiz, x.ruta)}:{x.numero}: {x.linea.Trim()}")
                .ToList();

        [Fact]
        public void Source_NeverReadsTheSystemClockDirectly()
        {
            // Arrange / Act: every "now" comes from the injected TimeProvider
            var lecturas = Coincidencias(Proyectos, LecturaDeReloj());

            // Assert
            Assert.True(lecturas.Count == 0, "Direct clock reads (use TimeProvider):" + Environment.NewLine + string.Join(Environment.NewLine, lecturas));
        }

        [Fact]
        public void Source_NeverThrowsArgumentExceptionForBusinessRules()
        {
            // Arrange / Act: business rules throw ReglaDeNegocioException; ArgumentNullException (a bug) is still allowed
            var lanzamientos = Coincidencias(Proyectos, LanzaArgumentException());

            // Assert
            Assert.True(lanzamientos.Count == 0, "Use ReglaDeNegocioException instead:" + Environment.NewLine + string.Join(Environment.NewLine, lanzamientos));
        }

        [Fact]
        public void Source_NeverDeletesRows_OnlyTheDemoResetMay()
        {
            // Arrange / Act: every physical delete in the sources
            var borrados = Coincidencias(Proyectos, BorraFilas());

            // Assert: the one exception is the demo reset (DemoData:ResetOnStartup), which wipes synthetic data and the history that
            // describes it. Real data is never deleted.
            Assert.Contains(borrados, b => b.Contains("DemoDataSeeder.cs", StringComparison.Ordinal)); // the scan sees the demo reset: it is not looking at nothing
            borrados.AddRange(Coincidencias(["DairyControl.Infrastructure"], RemueveEnInfraestructura()));
            var noPermitidos = borrados.Where(b => !b.Contains("DemoDataSeeder.cs", StringComparison.Ordinal)).ToList();
            Assert.True(noPermitidos.Count == 0, "Physical deletes (correct, void or deactivate instead):" + Environment.NewLine + string.Join(Environment.NewLine, noPermitidos));
        }

        [Fact]
        public void DemoReset_ClearsTheHistoryBeforeTheRecepciones()
        {
            // Arrange / Act: the history has no foreign key to what it describes, so the reset has to clear it by hand
            var fuente = File.ReadAllText(Path.Combine(Proyecto.Raiz, "src", "DairyControl.Infrastructure", "Persistence", "DemoDataSeeder.cs"));
            var historial = fuente.IndexOf("HistorialCambios.ExecuteDeleteAsync", StringComparison.Ordinal);
            var recepciones = fuente.IndexOf("Recepciones.ExecuteDeleteAsync", StringComparison.Ordinal);

            // Assert
            Assert.True(historial >= 0 && recepciones >= 0 && historial < recepciones, "The demo reset must delete the history before the recepciones.");
        }

        [Fact]
        public void SourceScan_FindsTheProjectsFiles()
        {
            // Arrange / Act
            var cantidad = Proyectos.Sum(p => Proyecto.Fuentes(p).Count());

            // Assert: guards against a scan that silently looks at nothing
            Assert.True(cantidad > 50, "Expected the source files of src, found " + cantidad);
        }
    }
}
