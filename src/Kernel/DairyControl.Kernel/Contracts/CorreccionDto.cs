namespace DairyControl.Kernel.Contracts;

public class CorreccionDto
{
    public Guid CorreccionId { get; set; }
    public DateTime EnUtc { get; set; }
    public string? Usuario { get; set; }
    public string Motivo { get; set; } = null!;
    public List<CampoCambiadoDto> Cambios { get; set; } = [];
}
