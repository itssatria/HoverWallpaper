namespace HoverWallpaper;

// Reads a zone's optional hold key and answers whether it is down right now.
// Keys are stored by winforms Keys name ("ControlKey", "A", "F9") so settings.json
// stays readable and does not depend on keyboard layouts.
public static class HoldKeys
{
    // Keys that should register even while the app is not focused, which is always
    // the case for a desktop wallpaper attached behind the icons.
    public const int VkShift = 0x10;
    public const int VkControl = 0x11;
    public const int VkMenu = 0x12;   // Alt
    public const int VkCapsLock = 0x14;
    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;

    public static bool TryParse(string? name, out System.Windows.Forms.Keys key)
    {
        key = System.Windows.Forms.Keys.None;
        if (string.IsNullOrWhiteSpace(name)) return false;
        // Enum.TryParse accepts "ControlKey, A" and numeric strings, which would quietly
        // turn into an unrelated key. Only exact enum names are accepted.
        if (!Enum.TryParse(name.Trim(), ignoreCase: true, out key)) return false;
        string? canonical = Enum.GetName(key);
        if (canonical == null || !canonical.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            key = System.Windows.Forms.Keys.None;
            return false;
        }
        if (key == System.Windows.Forms.Keys.None) return false;
        if ((key & System.Windows.Forms.Keys.Modifiers) != 0) return false;
        return true;
    }

    /// <summary>True when the zone has no key (always allowed) or its key is held.</summary>
    public static bool IsDown(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        if (!TryParse(name, out var key)) return true; // unreadable key never blocks hovering
        return IsDown(key);
    }

    public static bool IsDown(System.Windows.Forms.Keys key)
    {
        int vk = (int)key;
        if (vk is VkShift or VkControl or VkMenu or VkCapsLock or VkLWin or VkRWin)
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        // Toggle keys report their latched state through bit 0 and would otherwise
        // also need GetKeyState; keep them simple and treat them as held.
        if (vk is 0x90 or 0x91) // NumLock, ScrollLock
            return (GetKeyState(vk) & 0x0001) != 0;
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    /// <summary>Friendly text for the settings UI, e.g. "Ctrl" or "F9".</summary>
    public static string Describe(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "No key";
        if (!TryParse(name, out var key)) return name;
        return Describe(key);
    }

    public static string Describe(System.Windows.Forms.Keys key) => key switch
    {
        System.Windows.Forms.Keys.ShiftKey => "Shift",
        System.Windows.Forms.Keys.ControlKey => "Ctrl",
        System.Windows.Forms.Keys.Menu => "Alt",
        System.Windows.Forms.Keys.LWin => "Left Win",
        System.Windows.Forms.Keys.RWin => "Right Win",
        System.Windows.Forms.Keys.CapsLock => "Caps Lock",
        System.Windows.Forms.Keys.NumLock => "Num Lock",
        System.Windows.Forms.Keys.Scroll => "Scroll Lock",
        System.Windows.Forms.Keys.Space => "Space",
        System.Windows.Forms.Keys.Tab => "Tab",
        System.Windows.Forms.Keys.Enter => "Enter",
        System.Windows.Forms.Keys.Oemtilde => "`",
        System.Windows.Forms.Keys.OemMinus => "-",
        System.Windows.Forms.Keys.Oemplus => "=",
        System.Windows.Forms.Keys.OemOpenBrackets => "[",
        System.Windows.Forms.Keys.OemCloseBrackets => "]",
        System.Windows.Forms.Keys.OemPipe => "\\",
        System.Windows.Forms.Keys.OemSemicolon => ";",
        System.Windows.Forms.Keys.OemQuotes => "'",
        System.Windows.Forms.Keys.Oemcomma => ",",
        System.Windows.Forms.Keys.OemPeriod => ".",
        System.Windows.Forms.Keys.OemQuestion => "/",
        System.Windows.Forms.Keys.Back => "Backspace",
        System.Windows.Forms.Keys.Escape => "Esc",
        _ => key.ToString()
    };

    /// <summary>Only real, single keys can be assigned; modifier flag combos are rejected.</summary>
    public static bool IsAssignable(System.Windows.Forms.Keys key) =>
        key != System.Windows.Forms.Keys.None && (key & System.Windows.Forms.Keys.Modifiers) == 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern short GetKeyState(int vKey);
}
