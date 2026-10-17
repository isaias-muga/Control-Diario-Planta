namespace DairyControl.Application.DTOs
{
    public class ActualizarLimitesCalidadDto
    {
        public decimal AcidezMaxima { get; set; }
        public decimal TemperaturaMaxima { get; set; }
        public decimal AguaMaxima { get; set; }
        public int CantidadSilos { get; set; }
        /// <summary>The <c>version</c> received with the limits being edited; the service rejects a missing one.</summary>
        public string? Version { get; set; }
    }
}
