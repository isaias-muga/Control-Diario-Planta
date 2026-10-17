namespace DairyControl.Api.Authorization
{
    /// <summary>
    /// The authorization policies. Every controller action names one of them (an architecture test checks it); anything that names none
    /// falls to <see cref="Lectura"/>, so a forgotten attribute is never more open than "Admin or Oficina".
    /// </summary>
    public static class Politicas
    {
        /// <summary>Admin or Oficina: the office reads everything. This is also the default and the fallback policy.</summary>
        public const string Lectura = "Lectura";

        /// <summary>What the reception phone may also read: the proveedor options, the next ticket and the quality limits.</summary>
        public const string LecturaCarga = "LecturaCarga";

        /// <summary>Register a recepción: the office clerk (Oficina) records the plant lab measurement, Admin too, and the reception phone with an operario PIN.</summary>
        public const string Registro = "Registro";

        /// <summary>Everything that changes data other than registering a recepción, and the users and operarios.</summary>
        public const string Administracion = "Administracion";

        /// <summary>Any logged-in user (renew the session).</summary>
        public const string Autenticado = "Autenticado";

        /// <summary>Any logged-in user, even one that must change the password first (the only endpoint open in that state).</summary>
        public const string CambioPassword = "CambioPassword";
    }
}
