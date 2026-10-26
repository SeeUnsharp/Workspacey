using System.Diagnostics.CodeAnalysis;

namespace Workspacey.Core.Models;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
    Meta = 8,
}

/// <summary>
/// A global key combination such as <c>Meta+Shift+W</c>. Keys are identified by their Qt
/// key code because that is what kglobalaccel stores and transports over D-Bus.
/// </summary>
public sealed record KeyChord(KeyModifiers Modifiers, int QtKey)
{
    private const int QtShift = 0x02000000;
    private const int QtCtrl = 0x04000000;
    private const int QtAlt = 0x08000000;
    private const int QtMeta = 0x10000000;
    private const int QtKeypad = 0x20000000;
    private const int QtModifierMask = QtShift | QtCtrl | QtAlt | QtMeta | QtKeypad;

    /// <summary>
    /// A chord is a sensible global shortcut if it has a modifier, or if it is a function key
    /// (a bare letter would make that letter untypeable system-wide).
    /// </summary>
    public bool IsValidGlobalShortcut =>
        QtKeyNames.IsKnown(QtKey) && (Modifiers != KeyModifiers.None || QtKeyNames.IsFunctionKey(QtKey));

    /// <summary>Encodes the chord as a Qt 6 <c>QKeyCombination</c> integer.</summary>
    public int ToQtKeyCombination()
    {
        var code = QtKey;
        if (Modifiers.HasFlag(KeyModifiers.Shift)) code |= QtShift;
        if (Modifiers.HasFlag(KeyModifiers.Ctrl)) code |= QtCtrl;
        if (Modifiers.HasFlag(KeyModifiers.Alt)) code |= QtAlt;
        if (Modifiers.HasFlag(KeyModifiers.Meta)) code |= QtMeta;
        return code;
    }

    public static bool TryFromQtKeyCombination(int combination, [NotNullWhen(true)] out KeyChord? chord)
    {
        var key = combination & ~QtModifierMask;
        if (!QtKeyNames.IsKnown(key))
        {
            chord = null;
            return false;
        }

        var modifiers = KeyModifiers.None;
        if ((combination & QtShift) != 0) modifiers |= KeyModifiers.Shift;
        if ((combination & QtCtrl) != 0) modifiers |= KeyModifiers.Ctrl;
        if ((combination & QtAlt) != 0) modifiers |= KeyModifiers.Alt;
        if ((combination & QtMeta) != 0) modifiers |= KeyModifiers.Meta;
        chord = new KeyChord(modifiers, key);
        return true;
    }

    /// <summary>Formats the chord the way KDE does, e.g. <c>Meta+Shift+W</c>.</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Meta");
        if (Modifiers.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        parts.Add(QtKeyNames.GetName(QtKey));
        return string.Join('+', parts);
    }
}
