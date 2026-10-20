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

        /// <summary>
        /// Usernames of the owner accounts, separated by commas (AdminUser:Protegidos). Together with <see cref="Username"/> they are the
        /// protected accounts: another Admin cannot deactivate them, demote them or reset their password. Only configuration decides it;
        /// no endpoint can set or clear the flag.
        /// </summary>
        public string Protegidos { get; set; } = string.Empty;
    }
}
