using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Workspacey.Core.Abstractions;

namespace Workspacey.Kde.Desktop;

/// <summary>
/// Writes Workspacey's .desktop files to <c>~/.local/share/applications</c> and its icon to the hicolor
/// icon theme, then refreshes KDE's service cache (sycoca) so Plasma and kglobalaccel see the changes.
/// </summary>
public sealed class KdeDesktopIntegration(AppLauncher launcher, ILogger<KdeDesktopIntegration> logger) : IDesktopIntegration
{
    internal static string ApplicationsDirectory => Path.Combine(XdgPaths.DataHome, "applications");

    internal static string PickEntryPath => Path.Combine(ApplicationsDirectory, AppLauncher.PickDesktopFileName);

    private static string MenuEntryPath => Path.Combine(ApplicationsDirectory, AppLauncher.MenuDesktopFileName);

    private static string IconPath =>
        Path.Combine(XdgPaths.DataHome, "icons", "hicolor", "scalable", "apps", $"{AppLauncher.AppId}.svg");

    public bool IsMenuEntryInstalled => File.Exists(MenuEntryPath);

    public async Task InstallMenuEntryAsync(CancellationToken cancellationToken = default)
    {
        var iconInstalled = await InstallIconAsync(cancellationToken).ConfigureAwait(false);
        await WriteIfChangedAsync(MenuEntryPath, launcher.BuildMenuEntry(iconInstalled), cancellationToken).ConfigureAwait(false);
        await RebuildServiceCacheAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Installed application menu entry {Path}", MenuEntryPath);
    }

    public async Task RemoveMenuEntryAsync(CancellationToken cancellationToken = default)
    {
        File.Delete(MenuEntryPath);
        if (!File.Exists(PickEntryPath)) File.Delete(IconPath); // the shortcut launcher still uses it otherwise
        await RebuildServiceCacheAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Removed application menu entry {Path}", MenuEntryPath);
    }

    public async Task RefreshInstalledLaunchersAsync(CancellationToken cancellationToken = default)
    {
        var pickInstalled = File.Exists(PickEntryPath);
        if (!pickInstalled && !IsMenuEntryInstalled) return;

        var iconInstalled = await InstallIconAsync(cancellationToken).ConfigureAwait(false);
        var changed = false;
        if (pickInstalled)
        {
            changed |= await WriteIfChangedAsync(PickEntryPath, launcher.BuildPickEntry(iconInstalled), cancellationToken).ConfigureAwait(false);
        }

        if (IsMenuEntryInstalled)
        {
            changed |= await WriteIfChangedAsync(MenuEntryPath, launcher.BuildMenuEntry(iconInstalled), cancellationToken).ConfigureAwait(false);
        }

        if (changed)
        {
            logger.LogInformation("Updated installed launchers to start {Executable}", launcher.Executable);
            await RebuildServiceCacheAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Writes the hidden launcher the global shortcut is bound to.</summary>
    internal async Task InstallPickLauncherAsync(CancellationToken cancellationToken)
    {
        var iconInstalled = await InstallIconAsync(cancellationToken).ConfigureAwait(false);
        await WriteIfChangedAsync(PickEntryPath, launcher.BuildPickEntry(iconInstalled), cancellationToken).ConfigureAwait(false);

        // kglobalaccel resolves the launcher through KService, so the cache must know it before registering.
        await RebuildServiceCacheAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Copies the icon into the user's icon theme. Returns false if the app ships no icon.</summary>
    private async Task<bool> InstallIconAsync(CancellationToken cancellationToken)
    {
        if (launcher.IconSourcePath is not { } source || !File.Exists(source)) return File.Exists(IconPath);

        var content = await File.ReadAllTextAsync(source, cancellationToken).ConfigureAwait(false);
        await WriteIfChangedAsync(IconPath, content, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task<bool> WriteIfChangedAsync(string path, string content, CancellationToken cancellationToken)
    {
        if (File.Exists(path) && await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false) == content)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task RebuildServiceCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("kbuildsycoca6") { UseShellExecute = false });
            if (process is not null) await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            logger.LogWarning("kbuildsycoca6 not available ({Error}); launchers may only appear after re-login", e.Message);
        }
    }
}
