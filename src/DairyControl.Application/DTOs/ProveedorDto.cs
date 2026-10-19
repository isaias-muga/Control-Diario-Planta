namespace DairyControl.Application.DTOs
{
    public class ProveedorDto
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = null!;
        /// <summary>Recepciones that count (the voided ones are left out).</summary>
        public int CantidadRecepciones { get; set; }

        /// <summary>An inactive proveedor cannot be chosen for a new recepción but keeps all its history.</summary>
        public bool Activo { get; set; } = true;

        /// <summary>The row version (base64): a rename must send it back, so a stale edit is refused.</summary>
        public string? Version { get; set; }
    }
}