using DairyControl.Domain.Entities;
using DairyControl.Domain.Interfaces;
using Moq;

namespace DairyControl.Application.Tests
{
    internal static class UsuarioRepositorioFalso
    {
        /// <summary>Makes ModificarAsync apply the change to that user, like the real repository does (without the retry on a concurrent save).</summary>
        public static void AplicarModificaciones(this Mock<IUsuarioRepository> repositorio, Usuario usuario)
        {
            repositorio.Setup(r => r.ModificarAsync(usuario.Id, It.IsAny<Func<Usuario, bool>>()))
                .Returns<Guid, Func<Usuario, bool>>((_, cambio) =>
                {
                    cambio(usuario);
                    return Task.FromResult<Usuario?>(usuario);
                });
        }
    }
}
