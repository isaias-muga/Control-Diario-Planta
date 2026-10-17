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
            for (var i = 0; i < Usuario.MaxFallosLogin; i++)
            {
                usuario.RegistrarFalloLogin(Ahora);
            }

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
        public void RegistrarFalloLogin_FourFailures_DoesNotLock()
        {
            // Arrange
            var usuario = NuevoUsuario();

            // Act
            for (var i = 0; i < Usuario.MaxFallosLogin - 1; i++)
            {
                usuario.RegistrarFalloLogin(Ahora);
            }

            // Assert
            Assert.False(usuario.LoginBloqueado(Ahora));
        }

        [Fact]
        public void RegistrarFalloLogin_FiveFailures_LocksForFifteenMinutes()
        {
            // Arrange
            var usuario = NuevoUsuario();

            // Act
            for (var i = 0; i < Usuario.MaxFallosLogin; i++)
            {
                usuario.RegistrarFalloLogin(Ahora);
            }

            // Assert
            Assert.True(usuario.LoginBloqueado(Ahora));
            Assert.True(usuario.LoginBloqueado(Ahora.AddMinutes(14)));
            Assert.False(usuario.LoginBloqueado(Ahora.AddMinutes(15).AddSeconds(1)));
        }

        [Fact]
        public void ReiniciarFallosLogin_AfterFailures_StartsCountingAgain()
        {
            // Arrange
            var usuario = NuevoUsuario();
            for (var i = 0; i < Usuario.MaxFallosLogin - 1; i++)
            {
                usuario.RegistrarFalloLogin(Ahora);
            }

            // Act
            usuario.ReiniciarFallosLogin();
            usuario.RegistrarFalloLogin(Ahora);

            // Assert
            Assert.False(usuario.LoginBloqueado(Ahora));
        }

        [Fact]
        public void RegistrarPinIncorrecto_FiveWithinTheWindow_BlocksForFiveMinutes()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Recepcion);

            // Act
            for (var i = 0; i < Usuario.MaxFallosPin; i++)
            {
                usuario.RegistrarPinIncorrecto(Ahora.AddMinutes(i));
            }

            // Assert
            var ultimo = Ahora.AddMinutes(Usuario.MaxFallosPin - 1);
            Assert.True(usuario.PinBloqueado(ultimo));
            Assert.True(usuario.PinBloqueado(ultimo.AddMinutes(4)));
            Assert.False(usuario.PinBloqueado(ultimo.AddMinutes(5).AddSeconds(1)));
        }

        [Fact]
        public void RegistrarPinIncorrecto_FourWithinTheWindow_DoesNotBlock()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Recepcion);

            // Act
            for (var i = 0; i < Usuario.MaxFallosPin - 1; i++)
            {
                usuario.RegistrarPinIncorrecto(Ahora);
            }

            // Assert
            Assert.False(usuario.PinBloqueado(Ahora));
        }

        [Fact]
        public void RegistrarPinIncorrecto_FailuresSpreadOverMoreThanTheWindow_DoNotBlock()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Recepcion);
            for (var i = 0; i < Usuario.MaxFallosPin - 1; i++)
            {
                usuario.RegistrarPinIncorrecto(Ahora);
            }

            // Act: the fifth one comes after the 10-minute window of the first four
            usuario.RegistrarPinIncorrecto(Ahora.AddMinutes(11));

            // Assert
            Assert.False(usuario.PinBloqueado(Ahora.AddMinutes(11)));
        }

        [Fact]
        public void RegistrarPinCorrecto_AfterFailures_ResetsTheCount()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Recepcion);
            for (var i = 0; i < Usuario.MaxFallosPin - 1; i++)
            {
                usuario.RegistrarPinIncorrecto(Ahora);
            }

            // Act
            usuario.RegistrarPinCorrecto();
            usuario.RegistrarPinIncorrecto(Ahora);

            // Assert
            Assert.False(usuario.PinBloqueado(Ahora));
            Assert.Equal(1, usuario.FallosPin);
        }
    }
}
