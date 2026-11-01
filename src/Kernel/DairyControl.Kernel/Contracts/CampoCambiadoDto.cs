namespace DairyControl.Kernel.Contracts;

public class CampoCambiadoDto
{
    public string Campo { get; set; } = null!;
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
}
