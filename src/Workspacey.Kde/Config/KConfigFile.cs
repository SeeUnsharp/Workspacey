using System.Globalization;
using System.Text;

namespace Workspacey.Kde.Config;

/// <summary>
/// Minimal reader/writer for KDE's KConfig INI dialect. Group and entry order, unknown groups and
/// keys, and comment lines are preserved, so editing one rule never disturbs the rest of the file.
/// Values are exposed unescaped; KConfig's escaping (<c>\n</c>, <c>\t</c>, <c>\r</c>, <c>\\</c>,
/// <c>\s</c>, <c>\xNN</c>) is handled here.
/// </summary>
public sealed class KConfigFile
{
    private readonly List<string> _preamble = [];
    private readonly List<KConfigGroup> _groups = [];

    public IReadOnlyList<KConfigGroup> Groups => _groups;

    public static KConfigFile Parse(string text)
    {
        var file = new KConfigFile();
        KConfigGroup? current = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                // KConfig merges repeated groups (later values win); do the same so edits aren't shadowed.
                current = file.GetOrAddGroup(trimmed[1..^1]);
                continue;
            }

            if (current is null)
            {
                // Entries/comments before the first group are rare; keep them verbatim.
                if (trimmed.Length > 0) file._preamble.Add(line);
                continue;
            }

            if (trimmed.Length == 0) continue;

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (trimmed.StartsWith('#') || separator <= 0)
            {
                current.AddVerbatim(line);
                continue;
            }

            current.SetRaw(line[..separator].Trim(), line[(separator + 1)..].TrimStart());
        }

        return file;
    }

    public KConfigGroup? FindGroup(string name) =>
        _groups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.Ordinal));

    public KConfigGroup GetOrAddGroup(string name)
    {
        var group = FindGroup(name);
        if (group is null)
        {
            group = new KConfigGroup(name);
            _groups.Add(group);
        }

        return group;
    }

    public bool RemoveGroup(string name) =>
        _groups.RemoveAll(g => string.Equals(g.Name, name, StringComparison.Ordinal)) > 0;

    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var line in _preamble) builder.Append(line).Append('\n');

        foreach (var group in _groups)
        {
            if (builder.Length > 0) builder.Append('\n');
            builder.Append('[').Append(group.Name).Append("]\n");
            foreach (var line in group.Lines) builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Mirrors KConfig's <c>stringToPrintable</c>.</summary>
    internal static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\\': builder.Append(@"\\"); break;
                case '\n': builder.Append(@"\n"); break;
                case '\t': builder.Append(@"\t"); break;
                case '\r': builder.Append(@"\r"); break;
                case ' ' when i == 0 || i == value.Length - 1: builder.Append(@"\s"); break;
                case < ' ': builder.Append(CultureInfo.InvariantCulture, $@"\x{(int)c:x2}"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Mirrors KConfig's <c>printableToString</c>. Unknown escapes such as the <c>\d</c> in a
    /// hand-written regex are kept as-is (backslash included), exactly like KConfig does.
    /// <c>\xNN</c> denotes a raw byte of the UTF-8 encoded value.
    /// </summary>
    internal static string Unescape(string raw)
    {
        if (!raw.Contains('\\', StringComparison.Ordinal)) return raw;

        var input = Encoding.UTF8.GetBytes(raw);
        var output = new List<byte>(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] != '\\' || i == input.Length - 1)
            {
                output.Add(input[i]);
                continue;
            }

            var next = input[++i];
            switch (next)
            {
                case (byte)'\\': output.Add((byte)'\\'); break;
                case (byte)'n': output.Add((byte)'\n'); break;
                case (byte)'t': output.Add((byte)'\t'); break;
                case (byte)'r': output.Add((byte)'\r'); break;
                case (byte)'s': output.Add((byte)' '); break;
                case (byte)'x' when i + 2 < input.Length && IsHex(input[i + 1]) && IsHex(input[i + 2]):
                    output.Add(byte.Parse(Encoding.ASCII.GetString(input, i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 2;
                    break;
                default:
                    output.Add((byte)'\\');
                    output.Add(next);
                    break;
            }
        }

        return Encoding.UTF8.GetString([.. output]);
    }

    private static bool IsHex(byte b) => b is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F';
}

/// <summary>A <c>[group]</c> of a <see cref="KConfigFile"/>.</summary>
public sealed class KConfigGroup(string name)
{
    /// <summary>Entries in file order; <c>Key == null</c> marks a verbatim line such as a comment.</summary>
    private readonly List<(string? Key, string Raw)> _lines = [];

    public string Name { get; } = name;

    internal IEnumerable<string> Lines => _lines.Select(l => l.Key is null ? l.Raw : $"{l.Key}={l.Raw}");

    public bool ContainsKey(string key) => IndexOf(key) >= 0;

    public string? Get(string key)
    {
        var index = IndexOf(key);
        return index < 0 ? null : KConfigFile.Unescape(_lines[index].Raw);
    }

    public int? GetInt(string key) => int.TryParse(Get(key), out var value) ? value : null;

    public bool? GetBool(string key) => Get(key)?.Trim().ToUpperInvariant() switch
    {
        "TRUE" or "ON" or "YES" or "1" => true,
        "FALSE" or "OFF" or "NO" or "0" => false,
        _ => null,
    };

    /// <summary>Reads a KConfig string list (comma separated; <c>\,</c> and <c>\\</c> are escapes).</summary>
    public IReadOnlyList<string> GetList(string key)
    {
        var raw = Get(key);
        if (string.IsNullOrEmpty(raw)) return [];

        var items = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length && raw[i + 1] is ',' or '\\')
            {
                current.Append(raw[++i]);
            }
            else if (raw[i] == ',')
            {
                items.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(raw[i]);
            }
        }

        items.Add(current.ToString());
        return items;
    }

    public void Set(string key, string value) => SetRaw(key, KConfigFile.Escape(value));

    public void Set(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));

    public void SetList(string key, IEnumerable<string> items) =>
        Set(key, string.Join(',', items.Select(item => item
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace(",", @"\,", StringComparison.Ordinal))));

    public bool Remove(string key)
    {
        var index = IndexOf(key);
        if (index < 0) return false;
        _lines.RemoveAt(index);
        return true;
    }

    internal void SetRaw(string key, string rawValue)
    {
        var index = IndexOf(key);
        if (index >= 0) _lines[index] = (key, rawValue);
        else _lines.Add((key, rawValue));
    }

    internal void AddVerbatim(string line) => _lines.Add((null, line));

    private int IndexOf(string key) =>
        _lines.FindIndex(l => string.Equals(l.Key, key, StringComparison.Ordinal));
}
