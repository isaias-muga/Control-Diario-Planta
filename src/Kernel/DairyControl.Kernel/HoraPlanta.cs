namespace DairyControl.Kernel;

// The plant's wall-clock time. Dates in the system have no offset: they are always plant time.
public static class HoraPlanta
{
    public const string ZonaHoraria = "America/Argentina/Buenos_Aires";

    public static DateTime Ahora(TimeProvider reloj) =>
        TimeZoneInfo.ConvertTimeBySystemTimeZoneId(reloj.GetUtcNow().UtcDateTime, ZonaHoraria);

    // The UTC instant of a plant wall-clock time (used to stamp demo rows, whose delivery time is plant time).
    public static DateTime AUtc(DateTime fechaHoraPlanta) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(fechaHoraPlanta, DateTimeKind.Unspecified), TimeZoneInfo.FindSystemTimeZoneById(ZonaHoraria));

    // Dates have no offset and mean plant time. A caller that does send one (Z or +hh:mm) is converted to plant time.
    public static DateTime ALaPlanta(DateTime fechaHora) =>
        fechaHora.Kind == DateTimeKind.Unspecified
            ? fechaHora
            : TimeZoneInfo.ConvertTimeBySystemTimeZoneId(fechaHora.ToUniversalTime(), ZonaHoraria);
}
