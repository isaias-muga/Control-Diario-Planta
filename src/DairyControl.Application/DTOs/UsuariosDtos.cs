namespace DairyControl.Application.DTOs
{
    public class LoginResultadoDto
    {
        public string Token { get; set; } = string.Empty;

        /// <summary>True when the user must choose a new password before doing anything else.</summary>
        public bool DebeCambiarPassword { get; set; }
    }

    public class CambiarPasswordDto
    {
        public string PasswordActual { get; set; } = string.Empty;
        public string PasswordNueva { get; set; } = string.Empty;
    }

    public class UsuarioDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Rol { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public bool DebeCambiarPassword { get; set; }
    }

    public class CrearUsuarioDto
    {
        public string Username { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Rol { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class RestablecerPasswordDto
    {
        public string Password { get; set; } = string.Empty;
    }

    public class OperarioDto
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public bool Activo { get; set; }
    }

    public class CrearOperarioDto
    {
        public string Nombre { get; set; } = string.Empty;
        public string Pin { get; set; } = string.Empty;
    }

    public class CambiarPinDto
    {
        public string Pin { get; set; } = string.Empty;
    }

    /// <summary>What the reception phone needs to choose a proveedor: no counts.</summary>
    public class ProveedorOpcionDto
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }

    /// <summary>The answer to registering a recepción: what was saved and who identified as the operario, if anyone.</summary>
    public class RecepcionRegistradaDto
    {
        public Guid Id { get; set; }
        public int? Ticket { get; set; }
        public decimal Litros { get; set; }
        public string ProveedorNombre { get; set; } = string.Empty;
        public string? OperarioNombre { get; set; }
    }
}
