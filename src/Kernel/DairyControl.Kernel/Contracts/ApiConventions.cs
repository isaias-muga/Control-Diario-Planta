namespace DairyControl.Kernel.Contracts;

public static class ApiConventions
{
    public const string LoginRateLimitPolicy = "login";
    public const string MustChangePasswordCode = "debeCambiarPassword";

    public static class Claims
    {
        public const string Sub = "sub";
        public const string Rol = "role";
        public const string Sello = "stamp";
        public const string Nombre = "name";
    }
}
