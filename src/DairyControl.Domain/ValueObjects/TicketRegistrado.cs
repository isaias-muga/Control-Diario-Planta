namespace DairyControl.Domain.ValueObjects
{
    // Where a ticket number is already in use. A voided recepción keeps its ticket used.
    public sealed record TicketRegistrado
    {
        public TicketRegistrado(string proveedorNombre, DateTime fechaHora, bool anulada = false)
        {
            ProveedorNombre = proveedorNombre;
            FechaHora = fechaHora;
            Anulada = anulada;
        }

        public string ProveedorNombre { get; }
        public DateTime FechaHora { get; }
        public bool Anulada { get; }
    }
}
