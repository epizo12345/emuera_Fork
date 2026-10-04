namespace MinorShift.Emuera.Web.Runtime;

public sealed record BrowserKeyInput(int Code, int KeyData);

public static class BrowserInputMapping
{
    public static BrowserKeyInput? MapKey(string code, string key, bool control, bool shift, bool alt)
    {
        if (code.StartsWith("Numpad", StringComparison.Ordinal) && key is "End" or "Home" or "Insert" or "Delete" or "PageUp" or "PageDown" or "ArrowLeft" or "ArrowRight" or "ArrowUp" or "ArrowDown" or "Clear")
            return MapKey(key, key, control, shift, alt);
        int value = code switch
        {
            "ShiftLeft" or "ShiftRight" => 16,
            "ControlLeft" or "ControlRight" => 17,
            "AltLeft" or "AltRight" => 18,
            "Enter" or "NumpadEnter" => 13,
            "Escape" => 27,
            "Space" => 32,
            "ArrowLeft" => 37,
            "ArrowUp" => 38,
            "ArrowRight" => 39,
            "ArrowDown" => 40,
            "Backspace" => 8,
            "Tab" => 9,
            "Delete" => 46,
            "Home" => 36,
            "End" => 35,
            "PageUp" => 33,
            "PageDown" => 34,
            "Insert" => 45,
            "Clear" => 12,
            _ when code.Length >= 2 && code.StartsWith('F') && int.TryParse(code.AsSpan(1), out int f) && f is >= 1 and <= 12 => 111 + f,
            _ when code.Length == 7 && code.StartsWith("Numpad", StringComparison.Ordinal) && char.IsAsciiDigit(code[6]) => 96 + code[6] - '0',
            _ when code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal) && char.IsAsciiLetter(code[3]) => char.ToUpperInvariant(code[3]),
            _ when code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal) && char.IsAsciiDigit(code[5]) => code[5],
            _ when key.Length == 1 => char.ToUpperInvariant(key[0]),
            _ => 0
        };
        if (value == 0) return null;
        int keyData = value | (shift ? 65536 : 0) | (control ? 131072 : 0) | (alt ? 262144 : 0);
        return new(value, keyData);
    }
}
