# Bug Fixes by MebiusTos

A collection of bug fixes for Sea Power, loaded through Anchor Chain. Current version: **0.1.1**.

## Installation

1. Install Anchor Chain and the BepInEx setup it requires.
2. Close Sea Power. Extract the release ZIP and copy the enclosed `MebiusTosBugFixes` folder into the game's `Sea Power_Data/StreamingAssets/` directory. If you build from source, use `dist/MebiusTosBugFixes`.
3. Enable **Anchor Chain** and **Bug Fixes by MebiusTos** in the game's mod manager, then restart Sea Power.

To disable the mod, uncheck it in the mod manager and restart the game. To uninstall it, close the game and move `Sea Power_Data/StreamingAssets/MebiusTosBugFixes` outside the game directory.

The plugin ID is `mebiustos.seapower.bugfixes`, and the package folder is `MebiusTosBugFixes`. The mod does not modify the original game DLL or UI assets.

## Included fixes

### WorldViewWindowHost

Fixes the world-view window failing to appear when opened from the full-screen map after opening and closing the Esc pause menu.

The game updates the shared `WindowHostManager.CurrentHost` reference whenever it creates a `WindowHost`. Closing the pause menu does not restore that reference to the battle view. Because `MainGameViewModel.SyncWorldViewWindow()` uses the shared reference, it can add the world-view window to the hidden pause menu instead of the visible battle view.

This fix replaces only the method's host lookup with the `WindowHost` attached to the battle view itself. The game still handles visibility, dedicated-monitor output, window ordering, and open/close commands. The shared `CurrentHost` reference is left unchanged. Hosts are cached per battle view and rediscovered when its root is replaced.

The game's world-view menu label reports the current open/closed state. This mod does not change that text.

To reproduce the original issue with this mod disabled:

1. Set the dedicated world-view monitor to **None**.
2. Start a new battle without opening the world-view window first.
3. Open the pause menu with Esc, then return to the battle.
4. Switch the map to full screen and toggle the world view from the map menu.

The reported symptom is that the menu state changes but no window frame or title bar appears. As a comparison, start another new battle and try the same operation before opening the Esc menu. Use a fresh battle for each comparison: a window that was already displayed may have a different registration state.

## Disabling individual fixes

Copy `fixes.ini.example` to `fixes.ini` inside the installed mod folder, then set a fix to `false` to disable it:

```ini
[Fixes]
WorldViewWindowHost = false
```

All fixes are enabled when `fixes.ini` is absent. A fix omitted from the file is also enabled. Restart the game after changing these settings.

Unknown fix IDs, duplicate entries, and invalid Boolean values are logged as configuration errors and stop initialization. The input file is not overwritten.

Disabling the package in the game's mod manager prevents all of its fixes from being installed, regardless of the individual settings.

## Project structure and extension

- `src/Plugin.cs`: Anchor Chain entry point, package activation check, and configuration loading.
- `src/BugFixes.cs`: installation and rollback for individual fix modules, each with its own Harmony owner ID.
- `src/Fixes/WorldView/`: the world-view patch, battle-owned host lookup, and host cache.
- `tests/`: regression checks for settings, activation, host ownership, and the installed game's method instructions.
- `assets/workshop/workshop_icon.jpg`: the Steam Workshop icon, preserved as the supplied JPEG without recompression.

Add future fixes as independent `IBugFix` implementations and register them in `Plugin`. If a fix fails to install, the framework logs the error, removes that fix's patches, and continues with the other fixes.

Exceptions from initialization, patch generation, runtime host discovery, and logging are contained within this mod instead of propagating to Anchor Chain, another mod's Harmony call, or the game's update loop.

If the target method has an unsupported layout, the transpiler logs a compatibility error and returns the incoming instructions unchanged. An unsuccessful initial installation is rolled back. This exception boundary also applies when another mod later triggers a Harmony rebuild.

If the battle host cannot be resolved, the error is logged and that world-view synchronization is skipped. The fix does not register the window in an unrelated shared host. Repeated reports of the same failure are suppressed, and discovery can recover when the host becomes available or the UI root is replaced. Logging failures are also contained.

## Building and verification

Use the Windows .NET SDK. The test runner targets .NET 10, and the plugin targets .NET Standard 2.1. Copy `Directory.Build.local.props.example` to `Directory.Build.local.props` and set the game and Anchor Chain paths. No NuGet downloads are required.

```powershell
.\build.ps1 -Test
.\build.ps1 -Test -InstallLocal
```

`-Test` runs configuration, ownership, and game-method regression checks on .NET. It also invokes a separate process using the game's bundled Mono runtime to run those checks and verify Harmony patch installation and removal against the actual game method. The installed Harmony patching backend is intended for the game's Mono runtime. This separate process does not attach to a running game.

The regression fixture reads the actual instructions of `SyncWorldViewWindow()` from the installed game DLL. It substitutes managed window and command boundaries, then compares the original method's registration in the pause-menu host with the patched method's registration in the battle host. The game's algorithm is not copied into the tests.

These checks do not initialize Unity or Noesis rendering. In-game verification should cover opening and closing the window both before and after using the Esc menu, repeated toggles without duplicate windows, and dedicated-monitor output. Actual rendering and dedicated-monitor behavior remain unverified by the automated checks.
