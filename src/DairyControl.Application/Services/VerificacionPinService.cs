using DairyControl.Application.Seguridad;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;

namespace DairyControl.Application.Services
{
    /// <summary>Identifies the operario behind the reception phone: the phone logs in as one shared user, the PIN says who is loading.</summary>
    public class VerificacionPinService
    {
        private readonly IUsuarioRepository _usuarios;
        private readonly IOperarioRepository _operarios;
        private readonly IHasherDePin _hasher;
        private readonly IUsuarioActual _actual;
        private readonly TimeProvider _reloj;

        public VerificacionPinService(IUsuarioRepository usuarios, IOperarioRepository operarios, IHasherDePin hasher, IUsuarioActual actual, TimeProvider reloj)
        {
            _usuarios = usuarios;
            _operarios = operarios;
            _hasher = hasher;
            _actual = actual;
            _reloj = reloj;
        }

        /// <summary>
        /// For the Recepcion role: the PIN is required and must belong to an active operario, which is returned. After repeated wrong PINs
        /// the checks are refused for a while (the count is stored on the user, so it holds across API instances). Other roles need no PIN
        /// and get null.
        /// </summary>
        public async Task<Operario?> ResolverAsync(string? pin)
        {
            if (_actual.Rol != RolUsuario.Recepcion)
            {
                return null;
            }
            var usuario = _actual.Id is Guid id ? await _usuarios.GetByIdAsync(id) : null;
            if (usuario is null)
            {
                throw new ReglaDeNegocioException("No se pudo identificar al usuario.", "pin");
            }

            if (string.IsNullOrWhiteSpace(pin))
            {
                throw new ReglaDeNegocioException("Ingresá tu PIN de operario.", "pin");
            }
            // A malformed PIN is a typing mistake, not a guess: it does not count
            Operario.ValidarPin(pin);

            // The attempt is counted BEFORE the PIN is checked, so a burst of parallel guesses cannot all see the same count
            var ahora = _reloj.GetUtcNow().UtcDateTime;
            var permitido = false;
            await _usuarios.ModificarAsync(usuario.Id, u =>
            {
                permitido = u.ReservarIntentoPin(ahora);
                return true;
            });
            if (!permitido)
            {
                throw ErrorDeBloqueo();
            }

            var operario = await _operarios.GetActivoPorPinHashAsync(_hasher.Hash(pin));
            if (operario is null)
            {
                var bloqueado = false;
                await _usuarios.ModificarAsync(usuario.Id, u =>
                {
                    bloqueado = u.ConfirmarPinIncorrecto(ahora);
                    return true;
                });
                throw bloqueado ? ErrorDeBloqueo() : new ReglaDeNegocioException("PIN incorrecto.", "pin");
            }

            // The right PIN gives its own attempt back; it does not give back the wrong ones
            await _usuarios.ModificarAsync(usuario.Id, u =>
            {
                u.ConfirmarPinCorrecto();
                return true;
            });
            return operario;
        }

        private static DemasiadosIntentosException ErrorDeBloqueo() =>
            new("Demasiados intentos con el PIN. Esperá unos minutos o avisá en la oficina.");
    }
}
