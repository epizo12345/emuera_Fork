namespace MinorShift.Emuera.Web.Runtime;

public sealed class UnsupportedRuntimeFeatureException(string feature, string? source = null)
    : NotSupportedException(source is null ? $"P1A未対応: {feature}" : $"P1A未対応: {feature} ({source})")
{
    public string Feature { get; } = feature;
    public string? SourceLocation { get; } = source;
}
