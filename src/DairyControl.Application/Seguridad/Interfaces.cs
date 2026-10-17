using DairyControl.Domain.Enums;

namespace DairyControl.Application.Seguridad
{
    /// <summary>Hashes and checks passwords. The algorithm lives in Infrastructure.</summary>
    public interface IHasherDePassword
    {
        string Hash(string password);
        bool Verify(string hash, string password);
    }

    /// <summary>Turns a PIN into the keyed hash that is stored and looked up. Only the server secret can reproduce it.</summary>
    public interface IHasherDePin
    {
        string Hash(string pin);
    }

    /// <summary>The person making the current request, taken from the validated token (implemented in Api).</summary>
    public interface IUsuarioActual
    {
        Guid? Id { get; }
        RolUsuario? Rol { get; }
    }
}
