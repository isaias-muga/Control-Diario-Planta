namespace DairyControl.Kernel.Contracts;

/// <summary>Every API error has one shape: { "error": "message", "field": "name" } (field only when there is one).</summary>
public static class ApiErrors
{
    public static object Body(string error, string? field = null) =>
        field is null ? new { error } : new { error, field };
}
