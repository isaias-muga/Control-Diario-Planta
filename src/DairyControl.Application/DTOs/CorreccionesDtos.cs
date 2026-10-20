namespace DairyControl.Application.DTOs
{
    /// <summary>A recepción as the correction form needs it, with the version that the correction must send back.</summary>
    public class RecepcionDetalleDto
    {
        public Guid Id { get; set; }
        public Guid ProveedorId { get; set; }
        public string ProveedorNombre { get; set; } = null!;
        public bool ProveedorActivo { get; set; } = true;
        public int? Ticket { get; set; }
        public DateTime FechaHora { get; set; }
        public decimal Litros { get; set; }
        public decimal? AguaPorcentaje { get; set; }
        public decimal? Grasa { get; set; }
        public decimal Acidez { get; set; }
        public decimal Temperatura { get; set; }
        public int? Silo { get; set; }
        public string? Observaciones { get; set; }
        public bool Anulada { get; set; }
        public string Version { get; set; } = null!;
    }

    /// <summary>The same fields as registering, plus who the recepción is for now, the reason and the version that was read.</summary>
    public class CorregirRecepcionDto
    {
        public Guid? ProveedorId { get; set; }
        public decimal? Grasa { get; set; }
        public decimal Acidez { get; set; }
        public decimal Temperatura { get; set; }
        public decimal Litros { get; set; }
        public DateTime? FechaHora { get; set; }
        public int? Silo { get; set; }
        public string? Observaciones { get; set; }
        public decimal? AguaPorcentaje { get; set; }
        public int? Ticket { get; set; }
        public string? Motivo { get; set; }
        public string? Version { get; set; }
    }

    /// <summary>Only a reason: void a recepción, deactivate or reactivate a proveedor.</summary>
    public class MotivoCambioDto
    {
        public string? Motivo { get; set; }
    }

    public class RenombrarProveedorDto
    {
        public string? Nombre { get; set; }
        public string? Motivo { get; set; }
        public string? Version { get; set; }
    }

    /// <summary>One field that changed, as text (the history keeps every value in an invariant form).</summary>
    public class CampoCambiadoDto
    {
        public string Campo { get; set; } = null!;
        public string? ValorAnterior { get; set; }
        public string? ValorNuevo { get; set; }
    }

    /// <summary>One correction (or the void, or the deactivation): who, when, why and the fields it changed.</summary>
    public class CorreccionDto
    {
        public Guid CorreccionId { get; set; }
        public DateTime EnUtc { get; set; }
        public string? Usuario { get; set; }
        public string Motivo { get; set; } = null!;
        public List<CampoCambiadoDto> Cambios { get; set; } = [];
    }
}
