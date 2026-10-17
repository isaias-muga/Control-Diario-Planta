using DairyControl.Application.Seguridad;
using Microsoft.AspNetCore.Identity;

namespace DairyControl.Infrastructure.Security
{
    /// <summary>Password hashing with ASP.NET Core Identity's PasswordHasher (PBKDF2, random salt, versioned format). Only the hasher is used, not Identity.</summary>
    public sealed class PasswordHasherPbkdf2 : IHasherDePassword
    {
        // The hasher takes a user object only to let apps customize it; it does not read it.
        private static readonly object SinUsuario = new();
        private readonly PasswordHasher<object> _hasher = new();

        public string Hash(string password) => _hasher.HashPassword(SinUsuario, password);

        public bool Verify(string hash, string password)
        {
            try
            {
                return _hasher.VerifyHashedPassword(SinUsuario, hash, password) != PasswordVerificationResult.Failed;
            }
            catch (FormatException)
            {
                // A stored value that is not a hash never verifies
                return false;
            }
        }
    }
}
