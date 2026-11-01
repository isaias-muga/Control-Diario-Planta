namespace DairyControl.Kernel;

/// <summary>Marks a published contract interface. The version is compared with docs/modulos.md by an architecture test.</summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public sealed class ContractVersionAttribute(string version) : Attribute
{
    public string Version { get; } = version;
}
