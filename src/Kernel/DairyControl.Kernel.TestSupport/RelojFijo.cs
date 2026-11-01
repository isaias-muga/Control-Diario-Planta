namespace DairyControl.Kernel.TestSupport;

/// <summary>A clock that always says the same instant, so tests never depend on the real time.</summary>
public sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => ahora;
}
