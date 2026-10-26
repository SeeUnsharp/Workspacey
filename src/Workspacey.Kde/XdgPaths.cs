namespace Workspacey.Kde;

/// <summary>XDG base directories as used by KDE.</summary>
internal static class XdgPaths
{
    public static string ConfigHome => FromEnvironment("XDG_CONFIG_HOME", ".config");

    public static string DataHome => FromEnvironment("XDG_DATA_HOME", Path.Combine(".local", "share"));

    public static string RuntimeDir =>
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } dir ? dir : Path.GetTempPath();

    private static string FromEnvironment(string variable, string homeRelativeFallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), homeRelativeFallback);
}
