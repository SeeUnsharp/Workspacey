using System.Collections.Frozen;

namespace Workspacey.Core.Models;

/// <summary>
/// The subset of <c>Qt::Key</c> codes usable for the pick shortcut, with KDE's portable names.
/// </summary>
public static class QtKeyNames
{
    private const int F1 = 0x01000030;
    private const int F35 = F1 + 34;

    private static readonly FrozenDictionary<int, string> Names = BuildNames();
    private static readonly FrozenDictionary<string, int> Codes =
        Names.ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(int qtKey) => Names.ContainsKey(qtKey);

    public static bool IsFunctionKey(int qtKey) => qtKey is >= F1 and <= F35;

    public static string GetName(int qtKey) =>
        Names.TryGetValue(qtKey, out var name) ? name : $"0x{qtKey:X}";

    public static bool TryGetCode(string name, out int qtKey) => Codes.TryGetValue(name, out qtKey);

    private static FrozenDictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>
        {
            [0x20] = "Space",
            [0x01000000] = "Esc",
            [0x01000001] = "Tab",
            [0x01000003] = "Backspace",
            [0x01000004] = "Return",
            [0x01000006] = "Ins",
            [0x01000007] = "Del",
            [0x01000008] = "Pause",
            [0x01000009] = "Print",
            [0x01000010] = "Home",
            [0x01000011] = "End",
            [0x01000012] = "Left",
            [0x01000013] = "Up",
            [0x01000014] = "Right",
            [0x01000015] = "Down",
            [0x01000016] = "PgUp",
            [0x01000017] = "PgDown",
        };

        for (var c = '0'; c <= '9'; c++) names[c] = c.ToString();
        for (var c = 'A'; c <= 'Z'; c++) names[c] = c.ToString();
        for (var i = 0; i <= F35 - F1; i++) names[F1 + i] = $"F{i + 1}";

        return names.ToFrozenDictionary();
    }
}
