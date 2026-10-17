namespace DairyControl.Application.Settings
{
    public class PinSettings
    {
        public const int SecretMinLength = 32;

        /// <summary>Key of the HMAC that protects the operario PINs. Rotating it invalidates every stored PIN.</summary>
        public string Secret { get; set; } = string.Empty;
    }
}
