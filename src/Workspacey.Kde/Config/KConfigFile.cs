using System.Text;

namespace Workspacey.Kde.Config;

/// <summary>
/// Minimal reader/writer for KDE's KConfig INI dialect. Group and entry order as well as unknown
/// groups and keys are preserved, so editing one rule never disturbs the rest of the file.
/// Values are exposed unescaped; escaping (<c>\n</c>, <c>\t</c>, <c>\\</c>, <c>\s</c>) is handled here.
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
                current = new KConfigGroup(trimmed[1..^1]);
                file._groups.Add(current);
                continue;
            }

            if (current is null)
            {
                // Entries/comments before the first group are rare; keep them verbatim.
                if (trimmed.Length > 0) file._preamble.Add(line);
                continue;
            }

            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0) continue;

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
            foreach (var (key, rawValue) in group.RawEntries)
            {
                builder.Append(key).Append('=').Append(rawValue).Append('\n');
            }
        }

        return builder.ToString();
    }

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
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }

    internal static string Unescape(string raw)
    {
        if (!raw.Contains('\\', StringComparison.Ordinal)) return raw;

        var builder = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '\\' || i == raw.Length - 1)
            {
                builder.Append(raw[i]);
                continue;
            }

            builder.Append(raw[++i] switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                's' => ' ',
                var other => other, // "\\" -> "\", unknown escapes keep their character
            });
        }

        return builder.ToString();
    }
}

/// <summary>A <c>[group]</c> of a <see cref="KConfigFile"/>.</summary>
public sealed class KConfigGroup(string name)
{
    private readonly List<KeyValuePair<string, string>> _entries = [];

    public string Name { get; } = name;

    internal IEnumerable<KeyValuePair<string, string>> RawEntries => _entries;

    public bool ContainsKey(string key) => IndexOf(key) >= 0;

    public string? Get(string key)
    {
        var index = IndexOf(key);
        return index < 0 ? null : KConfigFile.Unescape(_entries[index].Value);
    }

    public int? GetInt(string key) => int.TryParse(Get(key), out var value) ? value : null;

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

    public void Set(string key, int value) =>
        Set(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public void SetList(string key, IEnumerable<string> items) =>
        Set(key, string.Join(',', items.Select(item => item
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace(",", @"\,", StringComparison.Ordinal))));

    public bool Remove(string key)
    {
        var index = IndexOf(key);
        if (index < 0) return false;
        _entries.RemoveAt(index);
        return true;
    }

    internal void SetRaw(string key, string rawValue)
    {
        var index = IndexOf(key);
        if (index >= 0) _entries[index] = new(key, rawValue);
        else _entries.Add(new(key, rawValue));
    }

    private int IndexOf(string key) =>
        _entries.FindIndex(e => string.Equals(e.Key, key, StringComparison.Ordinal));
}
