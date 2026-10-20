// Mirrors the domain constants in DairyControl.Domain. The API is the authority;
// these exist only to give the user immediate feedback.

export const NOMBRE_MAX_LENGTH = 200; // Proveedor.NombreMaxLength
export const OBSERVACIONES_MAX_LENGTH = 500; // RecepcionLeche.ObservacionesMaxLength
// Plausible ranges (typing errors, not the quality limits below).
export const LITROS_MIN = 0.001; // ParametrosCalidad.LitrosMin
export const LITROS_MAX = 30000; // ParametrosCalidad.LitrosMax
export const GRASA_MIN = 1; // ParametrosCalidad.GrasaMin
export const GRASA_MAX = 10; // ParametrosCalidad.GrasaMax
export const ACIDEZ_MIN = 10; // ParametrosCalidad.AcidezMin
export const ACIDEZ_MAX = 30; // ParametrosCalidad.AcidezMax
export const TEMPERATURA_MIN = 0; // ParametrosCalidad.TemperaturaMin
export const TEMPERATURA_MAX = 45; // ParametrosCalidad.TemperaturaMax
export const SILO_MIN = 1; // RecepcionLeche.SiloMin
export const SILO_MAX = 20; // RecepcionLeche.SiloMax
export const FECHA_MIN = '2000-01-01T00:00'; // RecepcionLeche.FechaMinima (plant time)
export const FECHA_ADELANTO_MINUTOS = 60; // RecepcionLeche.AdelantoMaximoHoras = 1
export const RESUMEN_MAX_DIAS = 366; // ResumenRecepcionesAppService.MaxDias
export const AGUA_MAX_PORCENTAJE = 100; // ParametrosCalidad.AguaMaxPorcentaje
export const TICKET_MIN = 1; // RecepcionLeche ticket must be a positive integer
export const TICKET_MAX = 2147483647; // the ticket is an int in the API; a bigger number is rejected as unreadable JSON
export const ACIDEZ_LIMITE_MIN = 10; // LimitesCalidad.AcidezMinimaPermitida
export const ACIDEZ_LIMITE_MAX = 30; // LimitesCalidad.AcidezMaximaPermitida
export const TEMPERATURA_LIMITE_MIN = 0; // LimitesCalidad.TemperaturaMinimaPermitida
export const TEMPERATURA_LIMITE_MAX = 40; // LimitesCalidad.TemperaturaMaximaPermitida
export const AGUA_LIMITE_MIN = 0; // LimitesCalidad.AguaMinimaPermitida
export const AGUA_LIMITE_MAX = 10; // LimitesCalidad.AguaMaximaPermitida
export const PIN_LENGTH = 4; // Operario.PinLength
export const PASSWORD_MIN_LENGTH = 8; // Usuario.PasswordMinLength
export const PASSWORD_MAX_LENGTH = 128; // Usuario.PasswordMaxLength
export const USERNAME_MIN_LENGTH = 3; // Usuario.UsernameMinLength
export const USERNAME_MAX_LENGTH = 50; // Usuario.UsernameMaxLength
export const MOTIVO_MIN_LENGTH = 5; // MotivoDeCambio.MinLength
export const MOTIVO_MAX_LENGTH = 500; // MotivoDeCambio.MaxLength
export const NOMBRE_PERSONA_MAX_LENGTH = 100; // Usuario.NombreMaxLength and Operario.NombreMaxLength
