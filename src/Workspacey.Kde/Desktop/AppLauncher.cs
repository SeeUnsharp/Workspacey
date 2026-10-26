using System.Text;

namespace Workspacey.Kde.Desktop;

/// <summary>
/// How to start Workspacey from a .desktop file, plus the content of the launchers Workspacey installs.
/// </summary>
/// <param name="Executable">
/// Stable path of the program. For an AppImage this must be the .AppImage file (<c>$APPIMAGE</c>),
/// not the binary inside its temporary mount point.
/// </param>
/// <param name="BaseArguments">Arguments needed before any of our own (e.g. the dll for <c>dotnet app.dll</c>).</param>
/// <param name="IconSourcePath">The app's SVG icon, copied into the user's icon theme on install.</param>
public sealed record AppLauncher(string Executable, IReadOnlyList<string> BaseArguments, string? IconSourcePath)
{
    /// <summary>Command line switch that starts the window picker.</summary>
    public const string PickArgument = "--pick";

    /// <summary>Icon theme name; also the window class, so Plasma maps windows to the menu entry.</summary>
    public const string AppId = "workspacey";

    public const string PickDesktopFileName = "workspacey-pick.desktop";
    public const string MenuDesktopFileName = "workspacey.desktop";

    private const string FallbackIcon = "preferences-system-windows-actions";

    /// <summary>Hidden launcher the global shortcut runs; kglobalaccel binds shortcuts to .desktop files.</summary>
    internal string BuildPickEntry(bool iconInstalled) => $"""
        [Desktop Entry]
        Type=Application
        Name=Workspacey: pick window
        Comment=Select a window and choose the workspace it should always open on
        Exec={BuildExec(PickArgument)}
        Icon={(iconInstalled ? AppId : FallbackIcon)}
        NoDisplay=true
        StartupNotify=false
        StartupWMClass={AppId}
        X-KDE-GlobalAccel-CommandShortcut=true

        """;

    /// <summary>Visible application menu entry that opens the settings window.</summary>
    internal string BuildMenuEntry(bool iconInstalled) => $"""
        [Desktop Entry]
        Type=Application
        Name=Workspacey
        GenericName=Workspace Rules
        Comment=Send games and windows to a fixed workspace
        Exec={BuildExec()}
        Icon={(iconInstalled ? AppId : FallbackIcon)}
        Terminal=false
        Categories=Utility;Settings;DesktopSettings;
        Keywords=workspace;virtual desktop;window rule;game;kwin;
        StartupWMClass={AppId}

        """;

    private string BuildExec(params string[] arguments) =>
        string.Join(' ', new[] { Executable }.Concat(BaseArguments).Concat(arguments).Select(QuoteExecArgument));

    /// <summary>Quoting rules of the desktop entry specification's <c>Exec</c> key.</summary>
    internal static string QuoteExecArgument(string argument)
    {
        // "%" starts a field code (%f, %u, …) anywhere in Exec, even inside quotes; "%%" is a literal "%".
        argument = argument.Replace("%", "%%", StringComparison.Ordinal);

        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || "\"'\\><~|&;$*?#()`".Contains(c)))
        {
            return argument;
        }

        var builder = new StringBuilder("\"");
        foreach (var c in argument)
        {
            if (c is '"' or '`' or '$' or '\\') builder.Append('\\');
            builder.Append(c);
        }

        // The Exec value itself is a KConfig string, so its backslashes are escaped once more.
        return builder.Append('"').ToString().Replace(@"\", @"\\", StringComparison.Ordinal);
    }
}
