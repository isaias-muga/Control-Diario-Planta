using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.Exceptions;

namespace DairyControl.Domain.Tests
{
    public class UsuarioTests
    {
        private static readonly DateTime Ahora = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);

        private static Usuario NuevoUsuario(RolUsuario rol = RolUsuario.Oficina) =>
            Usuario.Crear("maria.perez", "María Pérez", rol, "hash-1");

        [Fact]
        public void Crear_ValidData_IsActiveAndMustChangePassword()
        {
            // Arrange & Act
            var usuario = Usuario.Crear("  maria.perez  ", "  María Pérez ", RolUsuario.Oficina, "hash-1");

            // Assert
            Assert.NotEqual(Guid.Empty, usuario.Id);
            Assert.NotEqual(Guid.Empty, usuario.SecurityStamp);
            Assert.Equal("maria.perez", usuario.Username);
            Assert.Equal("María Pérez", usuario.Nombre);
            Assert.Equal(RolUsuario.Oficina, usuario.Rol);
            Assert.True(usuario.Activo);
            Assert.True(usuario.DebeCambiarPassword);
        }

        [Fact]
        public void Crear_WithoutObligationToChange_DoesNotRequirePasswordChange()
        {
            // Arrange & Act
            var usuario = Usuario.Crear("admin", "Administrador", RolUsuario.Admin, "hash-1", debeCambiarPassword: false);

            // Assert
            Assert.False(usuario.DebeCambiarPassword);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("ab")]
        [InlineData("con espacio")]
        [InlineData("acentó")]
        [InlineData("a@b.com")]
        public void Crear_InvalidUsername_ThrowsReglaDeNegocioException(string username)
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Usuario.Crear(username, "Nombre", RolUsuario.Oficina, "hash-1"));

            // Assert
            Assert.Equal("username", ex.Campo);
        }

        [Fact]
        public void Crear_UsernameTooLong_ThrowsReglaDeNegocioException()
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() =>
                Usuario.Crear(new string('a', Usuario.UsernameMaxLength + 1), "Nombre", RolUsuario.Oficina, "hash-1"));

            // Assert
            Assert.Equal("username", ex.Campo);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Crear_BlankNombre_ThrowsReglaDeNegocioException(string nombre)
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Usuario.Crear("maria", nombre, RolUsuario.Oficina, "hash-1"));

            // Assert
            Assert.Equal("nombre", ex.Campo);
        }

        [Fact]
        public void Crear_UndefinedRol_ThrowsReglaDeNegocioException()
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Usuario.Crear("maria", "María", (RolUsuario)99, "hash-1"));

            // Assert
            Assert.Equal("rol", ex.Campo);
        }

        [Fact]
        public void Crear_EmptyHash_ThrowsReglaDeNegocioException()
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Usuario.Crear("maria", "María", RolUsuario.Oficina, " "));

            // Assert
            Assert.Equal("password", ex.Campo);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("corta1")]
        public void ValidarPassword_TooShortOrEmpty_ThrowsReglaDeNegocioException(string? password)
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Usuario.ValidarPassword(password));

            // Assert
            Assert.Equal("password", ex.Campo);
        }

        [Fact]
        public void ValidarPassword_TooLong_ThrowsReglaDeNegocioException()
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Usuario.ValidarPassword(new string('x', Usuario.PasswordMaxLength + 1)));

            // Assert
            Assert.Equal("password", ex.Campo);
        }

        [Fact]
        public void ValidarPassword_EightCharacters_IsAccepted()
        {
            // Act & Assert
            Usuario.ValidarPassword("12345678");
        }

        [Fact]
        public void Desactivar_ActiveUser_BecomesInactiveAndChangesTheStamp()
        {
            // Arrange
            var usuario = NuevoUsuario();
            var sello = usuario.SecurityStamp;

            // Act
            usuario.Desactivar();

            // Assert
            Assert.False(usuario.Activo);
            Assert.NotEqual(sello, usuario.SecurityStamp);
        }

        [Fact]
        public void CambiarPassword_ByTheUser_ClearsTheObligationAndChangesTheStamp()
        {
            // Arrange
            var usuario = NuevoUsuario();
            var sello = usuario.SecurityStamp;

            // Act
            usuario.CambiarPassword("hash-2");

            // Assert
            Assert.Equal("hash-2", usuario.PasswordHash);
            Assert.False(usuario.DebeCambiarPassword);
            Assert.NotEqual(sello, usuario.SecurityStamp);
        }

        [Fact]
        public void RestablecerPassword_ByAnAdmin_RequiresAChangeAndChangesTheStamp()
        {
            // Arrange
            var usuario = Usuario.Crear("maria", "María", RolUsuario.Oficina, "hash-1", debeCambiarPassword: false);
            var sello = usuario.SecurityStamp;

            // Act
            usuario.RestablecerPassword("hash-temporal");

            // Assert
            Assert.Equal("hash-temporal", usuario.PasswordHash);
            Assert.True(usuario.DebeCambiarPassword);
            Assert.NotEqual(sello, usuario.SecurityStamp);
        }

        [Fact]
        public void RestablecerPassword_LockedUser_UnlocksTheLogin()
        {
            // Arrange
            var usuario = NuevoUsuario();
            for (var i = 0; i <= Usuario.MaxFallosLogin; i++)
            {
                usuario.ReservarIntentoLogin(Ahora);
            }
            Assert.True(usuario.LoginBloqueado(Ahora));

            // Act
            usuario.RestablecerPassword("hash-temporal");

            // Assert
            Assert.False(usuario.LoginBloqueado(Ahora));
        }

        [Fact]
        public void CambiarRol_NewRol_ChangesRolAndStamp()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Oficina);
            var sello = usuario.SecurityStamp;

            // Act
            usuario.CambiarRol(RolUsuario.Admin);

            // Assert
            Assert.Equal(RolUsuario.Admin, usuario.Rol);
            Assert.NotEqual(sello, usuario.SecurityStamp);
        }

        [Fact]
        public void CambiarRol_UndefinedRol_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var usuario = NuevoUsuario();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => usuario.CambiarRol((RolUsuario)42));

            // Assert
            Assert.Equal("rol", ex.Campo);
        }

        [Fact]
        public void ReservarIntentoLogin_FirstFiveAttempts_AreAllowed()
        {
            // Arrange
            var usuario = NuevoUsuario();

            // Act & Assert
            for (var i = 0; i < Usuario.MaxFallosLogin; i++)
            {
                Assert.True(usuario.ReservarIntentoLogin(Ahora));
            }
            Assert.False(usuario.LoginBloqueado(Ahora)); // the lock starts when the next attempt finds the five used
        }

        [Fact]
        public void ReservarIntentoLogin_SixthAttempt_IsRefusedAndLocksForFifteenMinutes()
        {
            // Arrange
            var usuario = NuevoUsuario();
            for (var i = 0; i < Usuario.MaxFallosLogin; i++)
            {
                usuario.ReservarIntentoLogin(Ahora);
            }

            // Act
            var permitido = usuario.ReservarIntentoLogin(Ahora);

            // Assert
            Assert.False(permitido);
            Assert.True(usuario.LoginBloqueado(Ahora));
            Assert.True(usuario.LoginBloqueado(Ahora.AddMinutes(14)));
            Assert.False(usuario.LoginBloqueado(Ahora.AddMinutes(15).AddSeconds(1)));
        }

        [Fact]
        public void ReservarIntentoLogin_WhileLocked_IsRefused()
        {
            // Arrange
            var usuario = NuevoUsuario();
            for (var i = 0; i <= Usuario.MaxFallosLogin; i++)
            {
                usuario.ReservarIntentoLogin(Ahora);
            }

            // Act & Assert
            Assert.False(usuario.ReservarIntentoLogin(Ahora.AddMinutes(5)));
        }

        [Fact]
        public void ReservarIntentoLogin_AfterTheLockExpires_AllowsAgain()
        {
            // Arrange
            var usuario = NuevoUsuario();
            for (var i = 0; i <= Usuario.MaxFallosLogin; i++)
            {
                usuario.ReservarIntentoLogin(Ahora);
            }

            // Act & Assert
            Assert.True(usuario.ReservarIntentoLogin(Ahora.AddMinutes(16)));
        }

        [Fact]
        public void ReiniciarFallosLogin_AfterAttempts_StartsCountingAgain()
        {
            // Arrange
            var usuario = NuevoUsuario();
            for (var i = 0; i < Usuario.MaxFallosLogin; i++)
            {
                usuario.ReservarIntentoLogin(Ahora);
            }

            // Act
            usuario.ReiniciarFallosLogin();

            // Assert: five new attempts are allowed
            for (var i = 0; i < Usuario.MaxFallosLogin; i++)
            {
                Assert.True(usuario.ReservarIntentoLogin(Ahora));
            }
        }

        [Fact]
        public void ReservarIntentoPin_FiveAttemptsInTheWindow_AreAllowedAndTheSixthIsRefused()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Recepcion);

            // Act & Assert
            for (var i = 0; i < Usuario.MaxFallosPin; i++)
            {
                Assert.True(usuario.ReservarIntentoPin(Ahora.AddMinutes(i)));
            }
            var ultimo = Ahora.AddMinutes(Usuario.MaxFallosPin);
            Assert.False(usuario.ReservarIntentoPin(ultimo));
            Assert.True(usuario.PinBloqueado(ultimo));
            Assert.True(usuario.PinBloqueado(ultimo.AddMinutes(4)));
            Assert.False(usuario.PinBloqueado(ultimo.AddMinutes(5).AddSeconds(1)));
        }

        [Fact]
        public void ConfirmarPinIncorrecto_TheFifthWrongOne_BlocksAtOnce()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Recepcion);
            for (var i = 0; i < Usuario.MaxFallosPin - 1; i++)
            {
                usuario.ReservarIntentoPin(Ahora);
                Assert.False(usuario.ConfirmarPinIncorrecto(Ahora));
            }
            usuario.ReservarIntentoPin(Ahora);

            // Act
            var bloqueado = usuario.ConfirmarPinIncorrecto(Ahora);

            // Assert
            Assert.True(bloqueado);
            Assert.True(usuario.PinBloqueado(Ahora));
        }

        [Fact]
        public void ReservarIntentoPin_AttemptsSpreadOverMoreThanTheWindow_DoNotAccumulate()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Recepcion);
            for (var i = 0; i < Usuario.MaxFallosPin - 1; i++)
            {
                usuario.ReservarIntentoPin(Ahora);
            }

            // Act: the next one comes after the ten minutes of the window, which starts counting again
            var permitido = usuario.ReservarIntentoPin(Ahora.AddMinutes(11));

            // Assert
            Assert.True(permitido);
            Assert.Equal(1, usuario.FallosPin);
        }

        [Fact]
        public void ConfirmarPinCorrecto_GivesBackItsOwnAttemptOnly()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Recepcion);
            for (var i = 0; i < 4; i++)
            {
                usuario.ReservarIntentoPin(Ahora); // four wrong guesses stay counted
            }
            usuario.ReservarIntentoPin(Ahora); // the right PIN of a colleague

            // Act
            usuario.ConfirmarPinCorrecto();

            // Assert: the four wrong ones are still there, so the guesser cannot reset the allowance with a PIN of their own
            Assert.Equal(4, usuario.FallosPin);
            Assert.True(usuario.ReservarIntentoPin(Ahora));
            Assert.False(usuario.PinBloqueado(Ahora));
        }

        [Fact]
        public void ConfirmarPinCorrecto_WithNoWrongAttempts_LeavesTheAllowanceUntouched()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Recepcion);

            // Act: ten loads in a row, each reserving and giving back its attempt
            for (var i = 0; i < 10; i++)
            {
                Assert.True(usuario.ReservarIntentoPin(Ahora));
                usuario.ConfirmarPinCorrecto();
            }

            // Assert
            Assert.Equal(0, usuario.FallosPin);
            Assert.Null(usuario.PrimerFalloPinUtc);
            Assert.False(usuario.PinBloqueado(Ahora));
        }
    }
}
