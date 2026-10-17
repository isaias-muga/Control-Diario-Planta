using DairyControl.Application.Seguridad;
using DairyControl.Application.Settings;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace DairyControl.Infrastructure.Security
{
    /// <summary>
    /// HMAC-SHA256 of the PIN keyed with the server secret (Pin:Secret). A PIN has only 10,000 values, so a plain hash would fall to a
    /// guess in a stolen database; with the key outside the database the stored values are useless on their own. Rotating the secret
    /// invalidates every stored PIN.
    /// </summary>
    public sealed class PinHasherHmac : IHasherDePin
    {
        private readonly byte[] _clave;

        public PinHasherHmac(IOptions<PinSettings> ajustes)
        {
            _clave = Encoding.UTF8.GetBytes(ajustes.Value.Secret);
        }

        public string Hash(string pin) => Convert.ToHexStringLower(HMACSHA256.HashData(_clave, Encoding.UTF8.GetBytes(pin)));
    }
}
