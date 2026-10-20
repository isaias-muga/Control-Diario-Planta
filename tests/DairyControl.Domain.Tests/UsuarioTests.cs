using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Domain.Tests
{
    public class UsuarioTests
    {
        private static readonly DateTime Ahora = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);
        private static readonly Guid AdminId = Guid.NewGuid();
        private static readonly MotivoDeCambio Motivo = MotivoDeCambio.Create("Cambio de tareas");

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
            var cambio = usuario.Desactivar(Motivo, AdminId, Ahora);

            // Assert
            Assert.False(usuario.Activo);
            Assert.NotEqual(sello, usuario.SecurityStamp);
            Assert.Equal(CambioRegistrado.EntidadUsuario, cambio.Entidad);
            Assert.Equal(usuario.Id, cambio.EntidadId);
            Assert.Equal(CambioRegistrado.Campos.Activo, cambio.Campo);
            Assert.Equal("Activo", cambio.ValorAnterior);
            Assert.Equal("Inactivo", cambio.ValorNuevo);
            Assert.Equal(AdminId, cambio.UsuarioId);
            Assert.Equal(Ahora, cambio.EnUtc);
            Assert.Equal("Cambio de tareas", cambio.Motivo);
        }

        [Fact]
        public void Desactivar_AlreadyInactive_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var usuario = NuevoUsuario();
            usuario.Desactivar(Motivo, AdminId, Ahora);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => usuario.Desactivar(Motivo, AdminId, Ahora));

            // Assert
            Assert.Equal("id", ex.Campo);
        }

        [Fact]
        public void Desactivar_ProtectedAccountByAnotherAdmin_ThrowsCuentaProtegidaAndStaysActive()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Admin);
            usuario.MarcarProtegido(true);
            var sello = usuario.SecurityStamp;

            // Act
            Assert.Throws<CuentaProtegidaException>(() => usuario.Desactivar(Motivo, AdminId, Ahora));

            // Assert
            Assert.True(usuario.Activo);
            Assert.Equal(sello, usuario.SecurityStamp);
        }

        [Fact]
        public void Reactivar_InactiveUser_IsActiveAgainMustChangePasswordAndChangesTheStamp()
        {
            // Arrange
            var usuario = Usuario.Crear("maria", "María", RolUsuario.Oficina, "hash-1", debeCambiarPassword: false);
            usuario.Desactivar(Motivo, AdminId, Ahora);
            var sello = usuario.SecurityStamp;

            // Act
            var cambio = usuario.Reactivar(MotivoDeCambio.Create("Volvió de licencia"), AdminId, Ahora);

            // Assert
            Assert.True(usuario.Activo);
            Assert.True(usuario.DebeCambiarPassword);
            Assert.NotEqual(sello, usuario.SecurityStamp);
            Assert.Equal(CambioRegistrado.Campos.Activo, cambio.Campo);
            Assert.Equal("Inactivo", cambio.ValorAnterior);
            Assert.Equal("Activo", cambio.ValorNuevo);
            Assert.Equal("Volvió de licencia", cambio.Motivo);
        }

        [Fact]
        public void Reactivar_LockedUser_UnlocksTheLogin()
        {
            // Arrange
            var usuario = NuevoUsuario();
            for (var i = 0; i <= Usuario.MaxFallosLogin; i++)
            {
                usuario.ReservarIntentoLogin(Ahora);
            }
            usuario.Desactivar(Motivo, AdminId, Ahora);

            // Act
            usuario.Reactivar(Motivo, AdminId, Ahora);

            // Assert
            Assert.False(usuario.LoginBloqueado(Ahora));
        }

        [Fact]
        public void Reactivar_AlreadyActive_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var usuario = NuevoUsuario();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => usuario.Reactivar(Motivo, AdminId, Ahora));

            // Assert
            Assert.Equal("id", ex.Campo);
        }

        [Fact]
        public void MarcarProtegido_Changes_ReturnsTrueOnlyWhenTheFlagChanged()
        {
            // Arrange
            var usuario = NuevoUsuario();

            // Act & Assert
            Assert.False(usuario.Protegido);
            Assert.True(usuario.MarcarProtegido(true));
            Assert.True(usuario.Protegido);
            Assert.False(usuario.MarcarProtegido(true));
            Assert.True(usuario.MarcarProtegido(false));
            Assert.False(usuario.Protegido);
        }

        [Fact]
        public void ValidarModificablePor_ProtectedAccountByItself_IsAllowed()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Admin);
            usuario.MarcarProtegido(true);

            // Act & Assert
            usuario.ValidarModificablePor(usuario.Id);
            Assert.Throws<CuentaProtegidaException>(() => usuario.ValidarModificablePor(AdminId));
        }

        [Fact]
        public void ValidarModificablePor_NotProtected_AnyAdminIsAllowed()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Admin);

            // Act & Assert
            usuario.ValidarModificablePor(AdminId);
        }

        [Theory]
        [InlineData(RolUsuario.Admin, true, true)]
        [InlineData(RolUsuario.Admin, false, false)]
        [InlineData(RolUsuario.Oficina, true, false)]
        [InlineData(RolUsuario.Consulta, true, false)]
        public void EsAdminActivo_RolAndState_OnlyAnActiveAdminCounts(RolUsuario rol, bool activo, bool esperado)
        {
            // Arrange
            var usuario = NuevoUsuario(rol);
            if (!activo)
            {
                usuario.Desactivar(Motivo, AdminId, Ahora);
            }

            // Act & Assert
            Assert.Equal(esperado, usuario.EsAdminActivo);
        }

        [Fact]
        public void RegistrarAlta_NewUser_WritesTheCreationEntryWithoutAnyPassword()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Consulta);

            // Act
            var cambio = usuario.RegistrarAlta(AdminId, Ahora);

            // Assert
            Assert.Equal(CambioRegistrado.EntidadUsuario, cambio.Entidad);
            Assert.Equal(usuario.Id, cambio.EntidadId);
            Assert.Equal(CambioRegistrado.Campos.Rol, cambio.Campo);
            Assert.Null(cambio.ValorAnterior);
            Assert.Equal("Consulta", cambio.ValorNuevo);
            Assert.Equal(AdminId, cambio.UsuarioId);
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
            var cambio = usuario.RestablecerPassword("hash-temporal", AdminId, Ahora);

            // Assert
            Assert.Equal("hash-temporal", usuario.PasswordHash);
            Assert.True(usuario.DebeCambiarPassword);
            Assert.NotEqual(sello, usuario.SecurityStamp);
            Assert.Equal(CambioRegistrado.Campos.Password, cambio.Campo);
            Assert.DoesNotContain("hash-temporal", cambio.ValorNuevo);
            Assert.Null(cambio.ValorAnterior);
            Assert.Equal(AdminId, cambio.UsuarioId);
        }

        [Fact]
        public void RestablecerPassword_ProtectedAccountByAnotherAdmin_ThrowsAndKeepsThePassword()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Admin);
            usuario.MarcarProtegido(true);

            // Act
            Assert.Throws<CuentaProtegidaException>(() => usuario.RestablecerPassword("hash-temporal", AdminId, Ahora));

            // Assert
            Assert.Equal("hash-1", usuario.PasswordHash);
        }

        [Fact]
        public void RestablecerPassword_ProtectedAccountByItself_IsAllowed()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Admin);
            usuario.MarcarProtegido(true);

            // Act
            usuario.RestablecerPassword("hash-temporal", usuario.Id, Ahora);

            // Assert
            Assert.Equal("hash-temporal", usuario.PasswordHash);
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
            usuario.RestablecerPassword("hash-temporal", AdminId, Ahora);

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
            var cambio = usuario.CambiarRol(RolUsuario.Admin, Motivo, AdminId, Ahora);

            // Assert
            Assert.Equal(RolUsuario.Admin, usuario.Rol);
            Assert.NotEqual(sello, usuario.SecurityStamp);
            Assert.Equal(CambioRegistrado.Campos.Rol, cambio.Campo);
            Assert.Equal("Oficina", cambio.ValorAnterior);
            Assert.Equal("Admin", cambio.ValorNuevo);
            Assert.Equal(AdminId, cambio.UsuarioId);
            Assert.Equal("Cambio de tareas", cambio.Motivo);
        }

        [Fact]
        public void CambiarRol_ProtectedAccountByAnotherAdmin_ThrowsAndKeepsTheRole()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Admin);
            usuario.MarcarProtegido(true);

            // Act
            Assert.Throws<CuentaProtegidaException>(() => usuario.CambiarRol(RolUsuario.Consulta, Motivo, AdminId, Ahora));

            // Assert
            Assert.Equal(RolUsuario.Admin, usuario.Rol);
        }

        [Fact]
        public void CambiarRol_SameRol_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Oficina);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => usuario.CambiarRol(RolUsuario.Oficina, Motivo, AdminId, Ahora));

            // Assert
            Assert.Equal("rol", ex.Campo);
        }

        [Fact]
        public void CambiarRol_ToConsulta_ChangesRolAndStamp()
        {
            // Arrange
            var usuario = NuevoUsuario(RolUsuario.Oficina);
            var sello = usuario.SecurityStamp;

            // Act
            usuario.CambiarRol(RolUsuario.Consulta, Motivo, AdminId, Ahora);

            // Assert
            Assert.Equal(RolUsuario.Consulta, usuario.Rol);
            Assert.NotEqual(sello, usuario.SecurityStamp);
        }

        [Fact]
        public void CambiarRol_UndefinedRol_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var usuario = NuevoUsuario();

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => usuario.CambiarRol((RolUsuario)42, Motivo, AdminId, Ahora));

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
