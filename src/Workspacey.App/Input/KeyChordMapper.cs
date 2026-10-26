using Avalonia.Input;
using Workspacey.Core.Models;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;
using CoreKeyModifiers = Workspacey.Core.Models.KeyModifiers;

namespace Workspacey.App.Input;

/// <summary>Translates Avalonia key events into Qt based <see cref="KeyChord"/>s.</summary>
internal static class KeyChordMapper
{
    private const int QtF1 = 0x01000030;

    /// <summary>
    /// Returns <c>null</c> for keys that cannot form a chord on their own (modifier keys, unsupported keys).
    /// </summary>
    public static KeyChord? Map(Key key, AvaloniaKeyModifiers modifiers)
    {
        var qtKey = ToQtKey(key);
        return qtKey is null ? null : new KeyChord(ToModifiers(modifiers), qtKey.Value);
    }

    private static CoreKeyModifiers ToModifiers(AvaloniaKeyModifiers modifiers)
    {
        var result = CoreKeyModifiers.None;
        if (modifiers.HasFlag(AvaloniaKeyModifiers.Shift)) result |= CoreKeyModifiers.Shift;
        if (modifiers.HasFlag(AvaloniaKeyModifiers.Control)) result |= CoreKeyModifiers.Ctrl;
        if (modifiers.HasFlag(AvaloniaKeyModifiers.Alt)) result |= CoreKeyModifiers.Alt;
        if (modifiers.HasFlag(AvaloniaKeyModifiers.Meta)) result |= CoreKeyModifiers.Meta;
        return result;
    }

    private static int? ToQtKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => 'A' + (key - Key.A),
        >= Key.D0 and <= Key.D9 => '0' + (key - Key.D0),
        >= Key.NumPad0 and <= Key.NumPad9 => '0' + (key - Key.NumPad0),
        >= Key.F1 and <= Key.F24 => QtF1 + (key - Key.F1),
        Key.Space => 0x20,
        Key.Tab => 0x01000001,
        Key.Back => 0x01000003,
        Key.Return => 0x01000004,
        Key.Insert => 0x01000006,
        Key.Delete => 0x01000007,
        Key.Pause => 0x01000008,
        Key.PrintScreen => 0x01000009,
        Key.Home => 0x01000010,
        Key.End => 0x01000011,
        Key.Left => 0x01000012,
        Key.Up => 0x01000013,
        Key.Right => 0x01000014,
        Key.Down => 0x01000015,
        Key.PageUp => 0x01000016,
        Key.PageDown => 0x01000017,
        _ => null,
    };
}
