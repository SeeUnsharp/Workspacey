using System.Text.RegularExpressions;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Workspacey.Tests;

/// <summary>
/// A <c>{DynamicResource}</c> with an unknown key fails silently at runtime: the property just stays
/// unset. That made all error messages invisible (their foreground brush did not exist).
/// </summary>
public partial class ThemeResourceTests
{
    public static TheoryData<string> ReferencedKeys()
    {
        var keys = new TheoryData<string>();
        foreach (var key in Directory.EnumerateFiles(AppSourceDirectory(), "*.axaml", SearchOption.AllDirectories)
                     .SelectMany(file => ResourceReference().Matches(File.ReadAllText(file)))
                     .Select(match => match.Groups["key"].Value)
                     .Distinct()
                     .Order(StringComparer.Ordinal))
        {
            keys.Add(key);
        }

        return keys;
    }

    [Theory]
    [MemberData(nameof(ReferencedKeys))]
    public void ResourceUsedInXaml_ExistsInFluentTheme(string key)
    {
        var theme = new FluentTheme();

        Assert.True(theme.TryGetResource(key, ThemeVariant.Light, out _), $"'{key}' is missing in the light theme");
        Assert.True(theme.TryGetResource(key, ThemeVariant.Dark, out _), $"'{key}' is missing in the dark theme");
    }

    private static string AppSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Workspacey.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Workspacey.slnx not found"),
            "src", "Workspacey.App");
    }

    [GeneratedRegex(@"\{(?:Dynamic|Static)Resource\s+(?<key>[A-Za-z0-9_.]+)\s*\}")]
    private static partial Regex ResourceReference();
}
