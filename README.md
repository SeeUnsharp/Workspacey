# Workspacey

Send games and windows to a fixed KDE Plasma workspace (virtual desktop), permanently.

1. Press your pick shortcut (e.g. `Meta+Shift+W`). The cursor turns into a crosshair.
2. Click the game or window.
3. Choose the workspace in the popup (or just press `1`–`9`).

Workspacey writes a regular **KWin window rule**, so from then on the window opens on that workspace,
even when Workspacey isn't running. The rules show up (and can be edited) in
*System Settings → Window Management → Window Rules*.

> **Note:** Workspacey is a hobby project I work on in my free time. Most of the code was generated
> with AI (Claude Code). I reviewed it and tested it on KDE Plasma 6, but please keep that in mind
> and report anything that looks off in the issues.

## Requirements

- KDE Plasma 6 (X11 or Wayland)
- .NET 10 SDK

## Build & run

```sh
dotnet build                                   # whole solution
dotnet test                                    # unit tests
dotnet run --project src/Workspacey.App        # settings window
dotnet run --project src/Workspacey.App -- --pick   # pick a window right away
```

## AppImage

```sh
./packaging/appimage/build-appimage.sh      # → artifacts/Workspacey-<version>-x86_64.AppImage
```

The AppImage is self-contained, so the target system doesn't need .NET. The build script downloads a
pinned `appimagetool` into `artifacts/tools` and verifies its checksum. Set
`APPIMAGETOOL=/path/to/appimagetool` to use your own copy.

The shortcut and the optional menu entry point to the `.AppImage` file. If you move or replace it,
open Workspacey once from the new location and the launchers update themselves. Before deleting the
AppImage, untick the menu option and clear the shortcut. Your workspace rules stay in KWin either way.

## Usage

In the settings window, click the shortcut button, press the key combination and click **Apply**.
The list below shows all KWin rules that assign windows to a workspace; **Delete** removes one.

In the popup:

- **Matching**: rules match the window class (for Steam games usually `steam_app_<id>`). Switch to
  *Contains* or *Regular expression* to cover several windows.
- **Behaviour**: *Open there* places the window when it opens (you can move it later); *force*
  keeps it on that workspace permanently.
- **Move the window there now** moves the window you picked right away as well.

## How it works

| Need | KDE mechanism |
|---|---|
| Crosshair window selection | KWin D-Bus `org.kde.KWin.queryWindowInfo` |
| Workspace list | KWin D-Bus `/VirtualDesktopManager` |
| Persistent placement | Rule group in `~/.config/kwinrulesrc` + `org.kde.KWin.reconfigure` |
| Move the picked window now | One-shot KWin script via `org.kde.kwin.Scripting` |
| Global shortcut | kglobalaccel + hidden `~/.local/share/applications/workspacey-pick.desktop` that runs `workspacey --pick` |
| Application menu entry (optional) | `~/.local/share/applications/workspacey.desktop` + icon in `~/.local/share/icons/hicolor` |
