namespace MinorShift.Emuera;

internal static class Program
{
    public static string ExeDir { get; set; } = string.Empty;
    public static string CsvDir { get; set; } = string.Empty;
    public static string ErbDir { get; set; } = string.Empty;
    public static string DatDir { get; set; } = string.Empty;
    public static string DebugDir { get; set; } = string.Empty;
    public static string ContentDir { get; set; } = string.Empty;
    public static bool DebugMode => false;
    public static bool AnalysisMode => false;
    public static IReadOnlyList<string> AnalysisFiles => Array.Empty<string>();
}
