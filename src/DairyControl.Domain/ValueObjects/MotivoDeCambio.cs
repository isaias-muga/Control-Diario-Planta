using DairyControl.Domain.Exceptions;

namespace DairyControl.Domain.ValueObjects
{
    /// <summary>Why someone corrected, voided or deactivated something: required, trimmed, at least <see cref="MinLength"/> characters.</summary>
    public record MotivoDeCambio
    {
        public const int MinLength = 5;
        public const int MaxLength = 500;

        public string Texto { get; }

        private MotivoDeCambio(string texto)
        {
            Texto = texto;
        }

        public static MotivoDeCambio Create(string? motivo)
        {
            var texto = motivo?.Trim();
            if (string.IsNullOrEmpty(texto))
            {
                throw new ReglaDeNegocioException("El motivo es obligatorio.", nameof(motivo));
            }
            if (texto.Length < MinLength)
            {
                throw new ReglaDeNegocioException($"El motivo debe tener al menos {MinLength} caracteres.", nameof(motivo));
            }
            if (texto.Length > MaxLength)
            {
                throw new ReglaDeNegocioException($"El motivo no puede superar los {MaxLength} caracteres.", nameof(motivo));
            }
            return new MotivoDeCambio(texto);
        }
    }
}
