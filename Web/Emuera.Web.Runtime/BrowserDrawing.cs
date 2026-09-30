namespace System.Drawing
{
    [Flags]
    public enum FontStyle { Regular = 0, Bold = 1, Italic = 2, Underline = 4, Strikeout = 8 }
}

namespace System.Media
{
    internal sealed class SystemSound { public void Play() { } }
    internal static class SystemSounds
    {
        public static SystemSound Hand { get; } = new();
        public static SystemSound Asterisk { get; } = new();
    }
}
