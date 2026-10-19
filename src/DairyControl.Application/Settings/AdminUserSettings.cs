namespace DairyControl.Application.Settings
{
    public class AdminUserSettings
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// The first admin must choose a password of their own at the first login (secure by default). Only the compose stack, the e2e
        /// tests and the demo set it to false.
        /// </summary>
        public bool ForzarCambioPassword { get; set; } = true;
    }
}
