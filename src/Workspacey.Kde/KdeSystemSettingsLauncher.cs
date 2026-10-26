using System.Diagnostics;
using Workspacey.Core.Abstractions;

namespace Workspacey.Kde;

/// <summary>Opens Plasma System Settings on a specific module (KCM).</summary>
public sealed class KdeSystemSettingsLauncher : ISystemSettingsLauncher
{
    private const string VirtualDesktopsModule = "kcm_kwin_virtualdesktops";

    public void OpenWorkspaceSettings() => Open(VirtualDesktopsModule);

    private static void Open(string module)
    {
        // Fire and forget: System Settings is a separate app the user closes on their own.
        using var process = Process.Start(new ProcessStartInfo("systemsettings", [module]) { UseShellExecute = false });
    }
}
