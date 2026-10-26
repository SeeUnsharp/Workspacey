# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```sh
dotnet build                                          # whole solution (Workspacey.slnx)
dotnet test                                           # all tests
dotnet test --filter "FullyQualifiedName~KConfigFileTests"            # one class
dotnet test --filter "FullyQualifiedName~KeyChordTests.MetaShiftW_EncodesLikeQt"  # one test
dotnet run --project src/Workspacey.App               # settings window
dotnet run --project src/Workspacey.App -- --pick     # pick mode (what the global shortcut runs)
./packaging/appimage/build-appimage.sh                # self-contained AppImage → artifacts/
```

The version lives in `Directory.Build.props` (`<Version>`); the AppImage file name is derived from it.

`TreatWarningsAsErrors` and `AnalysisLevel=latest-recommended` are on for every project (`Directory.Build.props`), so CA analyzer findings break the build. Package versions are managed centrally in `Directory.Packages.props`. Don't put versions in the csproj files.

## What it is

A KDE Plasma 6 tool: a global shortcut turns the cursor into a crosshair, the user clicks a window or game, and a popup asks which virtual desktop it belongs on. The result is persisted as a native **KWin window rule** in `~/.config/kwinrulesrc`, so placement works without Workspacey running.

## Architecture

Clean-architecture split. Dependencies point inward:

- **`Workspacey.Core`**: models, abstractions (`IWindowPicker`, `IVirtualDesktopService`, `IWorkspaceRuleRepository`, `IWindowManager`, `IPickShortcutService`) and the single use case `WorkspaceAssignmentService` (upsert rule → reload KWin → optionally move the live window). No KDE or UI dependencies.
- **`Workspacey.Kde`**: implementations over the session D-Bus (Tmds.DBus.Protocol, low-level API) and KDE config files. `ServiceCollectionExtensions.AddKdeIntegration` registers everything.
- **`Workspacey.App`**: Avalonia 12 + CommunityToolkit.Mvvm. `App.axaml.cs` is the DI composition root and chooses the startup mode: with `--pick` it runs `IPickWorkflow` headless and exits, otherwise it shows `MainWindow`.
- **`tests/Workspacey.Tests`**: xunit tests for Core and Kde. They don't touch D-Bus. Repository tests use temp files.

### KDE integration details (non-obvious)

- **kwinrulesrc format**: one group per rule, named by the rule id. `[General] Order=` lists the rule ids. Plasma ≤ 6.x used `rules=` + `count=`, which `KWinRulesRepository` keeps in sync only if they already exist. Rule keys: `wmclass`, `wmclassmatch` (1 exact, 2 substring, 3 regex), `desktops` (desktop uuids), `desktopsrule` (2 force, 3 apply initially). `KConfigFile` preserves unknown groups/keys/order; never rewrite the file wholesale. Writes are atomic (temp file + rename) and follow symlinks.
- **Window picking**: `org.kde.KWin /KWin queryWindowInfo` returns `a{sv}` (`uuid`, `caption`, `resourceClass`, …). Cancellation comes back as a D-Bus error reply, not an empty result.
- **Moving a live window**: KWin has no D-Bus method for this, so `KWinWindowManager` writes a JS file to `$XDG_RUNTIME_DIR`, then calls `loadScript` → `/Scripting/Script{id}.run` → waits → `unloadScript`. Script values are injected via `JsonSerializer.Serialize` to avoid JS injection.
- **D-Bus connection**: services share the auto-connecting `DBusConnection.Session`, which throws on `TrySendMessage` (fire-and-forget). Always use `DBusMethod.CallAsync`, even for Qt `Q_NOREPLY` methods like KWin's `reconfigure`: Qt still replies when a reply is expected.
- **Launchers** (`Kde/Desktop`): `AppLauncher` describes how to start the app. Inside an AppImage it uses `$APPIMAGE`, never `Environment.ProcessPath`, which points into a temporary mount. `KdeDesktopIntegration` writes the `.desktop` files and copies the icon into the hicolor theme. Every time the settings window opens, it rewrites launchers that are already installed to point at the running executable. So the last build you open the settings window from, including a VS Code debug run, owns the shortcut. Test with an isolated `XDG_DATA_HOME`/`XDG_CACHE_HOME` to avoid that.
- **Global shortcut**: there is no in-process key grab (impossible on Wayland). `KGlobalAccelPickShortcutService` has the hidden `workspacey-pick.desktop` launcher written (Exec = launcher + `--pick`), then registers action `[workspacey-pick.desktop, _launch, …]` with kglobalaccel. Components ending in `.desktop` are "service" components that kglobalaccel launches itself. Keys travel as `a(ai)` Qt `QKeyCombination` ints; `KeyChord`/`QtKeyNames` handle encoding. An empty result from `setShortcutKeys` means the combination is owned by another action.

### Conventions

- ViewModels never reference Avalonia types. Windows subscribe to VM events (`CloseRequested`), and key handling stays in code-behind where it needs Avalonia `Key` (mapped via `Input/KeyChordMapper`).
- Compiled bindings are on by default. Every XAML view and `DataTemplate` needs `x:DataType`.
- `[ObservableProperty]` is used on `partial` properties (CommunityToolkit.Mvvm 8.4 style), not on fields.
