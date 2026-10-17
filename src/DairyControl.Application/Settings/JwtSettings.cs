namespace DairyControl.Application.Settings
{
    public class JwtSettings
    {
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public string SigningKey { get; set; } = string.Empty;

        /// <summary>Session length of the reception phone, in days.</summary>
        public int RecepcionDias { get; set; } = 7;

        /// <summary>Session length of Admin and Oficina, in hours.</summary>
        public int PersonalHoras { get; set; } = 12;
    }
}
