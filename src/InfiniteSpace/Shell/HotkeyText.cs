using System.Windows.Input;

namespace InfiniteSpace.Shell;

/// <summary>Hotkeys as people write them ("Ctrl+Alt+`"), read from settings and shown in menus.</summary>
internal static class HotkeyText
{
    /// <summary>Reads "Ctrl+Alt+`", "Win+Alt+W", "ctrl + shift + F9"…; needs at least one modifier (a bare key would be taken from every app).</summary>
    public static bool TryParse(string? text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        Key? found = null;
        foreach (string raw in text.Split('+'))
        {
            string token = raw.Trim();
            if (found is not null)
                return false; // the key comes last, and only once
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= ModifierKeys.Control;
                    continue;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    continue;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    continue;
                case "win" or "windows":
                    modifiers |= ModifierKeys.Windows;
                    continue;
            }
            found = KeyOf(token);
            if (found is null)
                return false;
        }
        if (found is null || modifiers == ModifierKeys.None)
            return false;
        key = found.Value;
        return true;
    }

    /// <summary>"Ctrl+Alt+`": modifiers in the order Ctrl, Alt, Shift, Win.</summary>
    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows))
            parts.Add("Win");
        parts.Add(key switch
        {
            Key.Oem3 => "`",
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            _ => key.ToString(),
        });
        return string.Join("+", parts);
    }

    private static Key? KeyOf(string token)
    {
        if (token is "`" or "~" || token.Equals("oem3", StringComparison.OrdinalIgnoreCase))
            return Key.Oem3;
        if (token.Length == 1 && char.IsAsciiLetter(token[0]))
            return Key.A + (char.ToUpperInvariant(token[0]) - 'A');
        if (token.Length == 1 && char.IsAsciiDigit(token[0]))
            return Key.D0 + (token[0] - '0');
        if (token.Equals("space", StringComparison.OrdinalIgnoreCase))
            return Key.Space;
        return token.Length > 0 && !char.IsAsciiDigit(token[0]) && Enum.TryParse<Key>(token, ignoreCase: true, out var key) ? key : null;
    }
}
