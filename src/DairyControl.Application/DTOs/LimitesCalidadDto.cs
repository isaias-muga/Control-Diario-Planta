namespace DairyControl.Application.DTOs
{
    public class LimitesCalidadDto
    {
        public decimal AcidezMaxima { get; set; }
        public decimal TemperaturaMaxima { get; set; }
        public decimal AguaMaxima { get; set; }
        public int CantidadSilos { get; set; }
        public DateTime ActualizadoEn { get; set; }
        /// <summary>Opaque version (base64) of the limits as they are now; the update must send it back.</summary>
        public string Version { get; set; } = string.Empty;
    }
}
