using System.Reflection;

namespace MinorShift.Emuera.Runtime.Utils;

public static class AssemblyData
{
    static readonly Assembly Assembly = typeof(AssemblyData).Assembly;

    public static readonly string ExePath = Environment.ProcessPath ?? string.Empty;
    public static readonly string ExeName = Path.GetFileNameWithoutExtension(ExePath);
    public static readonly Version emueraVer = Assembly.GetName().Version ?? new Version(0, 0);
    public static readonly string EmueraVersionText = "Emuera.NET " +
        (Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? emueraVer.ToString());
}
