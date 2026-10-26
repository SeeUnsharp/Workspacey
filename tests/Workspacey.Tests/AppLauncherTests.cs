using Workspacey.Kde.Config;
using Workspacey.Kde.Desktop;

namespace Workspacey.Tests;

public class AppLauncherTests
{
    [Fact]
    public void PickEntry_RunsAppImageWithPickArgument()
    {
        var launcher = new AppLauncher("/home/me/Apps/My Apps/Workspacey-x86_64.AppImage", [], "/tmp/icon.svg");

        var entry = ParseEntry(launcher.BuildPickEntry(iconInstalled: true));

        Assert.Equal("\"/home/me/Apps/My Apps/Workspacey-x86_64.AppImage\" --pick", entry.Get("Exec"));
        Assert.Equal("workspacey", entry.Get("Icon"));
        Assert.Equal("true", entry.Get("NoDisplay"));
    }

    [Fact]
    public void MenuEntry_IsVisibleAndHasNoPickArgument()
    {
        var launcher = new AppLauncher("/usr/bin/dotnet", ["/opt/workspacey/workspacey.dll"], null);

        var entry = ParseEntry(launcher.BuildMenuEntry(iconInstalled: false));

        Assert.Equal("/usr/bin/dotnet /opt/workspacey/workspacey.dll", entry.Get("Exec"));
        Assert.Equal("preferences-system-windows-actions", entry.Get("Icon"));
        Assert.Null(entry.Get("NoDisplay"));
        Assert.Equal("workspacey", entry.Get("StartupWMClass"));
    }

    [Theory]
    [InlineData("/plain/path", "/plain/path")]
    [InlineData("/with space/app", "\"/with space/app\"")]
    [InlineData("/cost$/app", "\"/cost\\\\$/app\"")] // \$ in the Exec value, written as \\$ (KConfig escaping)
    [InlineData("/Apps/Workspacey%20v1.AppImage", "/Apps/Workspacey%%20v1.AppImage")] // % starts a field code
    [InlineData("/My Apps/100%", "\"/My Apps/100%%\"")]
    public void QuoteExecArgument_FollowsDesktopEntrySpec(string argument, string expected) =>
        Assert.Equal(expected, AppLauncher.QuoteExecArgument(argument));

    private static KConfigGroup ParseEntry(string text) => KConfigFile.Parse(text).FindGroup("Desktop Entry")!;
}
