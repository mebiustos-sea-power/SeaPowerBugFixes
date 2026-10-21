# Bug Fixes by MebiusTos

A collection of bug fixes for Sea Power, loaded through Anchor Chain. Current version: **0.1.3**.

[Source code](https://github.com/mebiustos-sea-power/SeaPowerBugFixes) · [Report an issue](https://github.com/mebiustos-sea-power/SeaPowerBugFixes/issues) · [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3821735974)

## Installation

1. Install Anchor Chain and the BepInEx setup it requires.
2. Close Sea Power. Extract the release ZIP and copy the enclosed `MebiusTosBugFixes` folder into the game's `Sea Power_Data/StreamingAssets/` directory. If you build from source, use `dist/MebiusTosBugFixes`.
3. Enable **Anchor Chain** and **Bug Fixes by MebiusTos** in the game's mod manager, then restart Sea Power.

To disable the mod, uncheck it in the mod manager and restart the game. To uninstall it, close the game and move `Sea Power_Data/StreamingAssets/MebiusTosBugFixes` outside the game directory.

The plugin ID is `mebiustos.seapower.bugfixes`, and the package folder is `MebiusTosBugFixes`. The mod does not modify the original game DLL or UI assets.

## Included fixes

### FormationUnitReselection

Fixes the formation manager failing to reselect the same friendly unit after an unidentified contact is selected on the map. The bottom panel can remain on the contact even though the formation manager and camera still point to the friendly unit. Selecting another friendly unit first works around the original issue.

The formation command normally relies on a camera change to update the selected unit. The camera switch returns immediately when it is already following that unit, so no selection notification reaches the bottom panel. Reassigning the same camera target would also suppress duplicate notifications.

This fix preserves the native selection command, then selects the requested friendly unit through the current battle map's standard selection transaction when needed. It is independent of whether the world-view window is visible and does not rewrite the camera method. The command is wrapped as it is assigned, before UI bindings receive it; its availability and change events are forwarded.

To reproduce the original issue with this fix disabled, select a friendly ship, select a contact whose side or class has not been identified, and double-click the same ship in the formation manager. With the fix enabled, the bottom panel and map selection return to the ship without needing to select another ship first.

## Disabling individual fixes

All fixes are enabled by default and can be disabled independently. In the installed mod folder (the folder containing `MebiusTosBugFixes.dll`), copy `fixes.ini.example` to `fixes.ini` and set the fix you want to disable to `false`.

For example, this configuration disables the formation-manager fix:

```ini
[Fixes]
FormationUnitReselection = false
```

Set a fix back to `true` to re-enable it. If `fixes.ini` is absent, all fixes are enabled; omitted entries are also enabled. **Restart Sea Power after changing these settings.** Preserve your `fixes.ini` when updating the mod.

Unknown fix IDs and invalid Boolean values produce a warning at startup, and the affected entries are ignored. This includes settings left behind by removed fixes. Ignoring an invalid value preserves the fix's default (enabled) or its previous valid value in the file; other fixes continue loading. Duplicate valid entries for a known fix and malformed configuration syntax are logged as configuration errors and stop initialization. The input file is not overwritten.

Disabling the package in the game's mod manager prevents all of its fixes from being installed, regardless of the individual settings.

## Project structure and extension

- `src/Plugin.cs`: Anchor Chain entry point, package activation check, and configuration loading.
- `src/BugFixes.cs`: installation and rollback for individual fix modules, each with its own Harmony owner ID.
- `src/Fixes/Selection/`: formation-command selection synchronization and contained command forwarding.
- `tests/`: regression checks for settings, activation, selection, and the installed game's method instructions.
- `assets/workshop/workshop_icon.jpg`: the Steam Workshop icon, preserved as the supplied JPEG without recompression.

Add future fixes as independent `IBugFix` implementations and register them in `Plugin`. If a fix fails to install, the framework logs the error, removes that fix's patches, and continues with the other fixes.

Exceptions from initialization, patch installation, selection-command synchronization, and logging are contained within this mod instead of propagating to Anchor Chain, another mod's Harmony call, or the game's update loop.

## Building and verification

Use the Windows .NET SDK. The test runner targets .NET 10, and the plugin targets .NET Standard 2.1. Copy `Directory.Build.local.props.example` to `Directory.Build.local.props` and set the game and Anchor Chain paths. No NuGet downloads are required.

```powershell
.\build.ps1 -Test
.\build.ps1 -Test -InstallLocal
```

`-Test` runs configuration, activation, and selection regression checks on .NET. It also invokes a separate process using the game's bundled Mono runtime to run those checks and verify Harmony patch installation and removal against the actual formation-command setter. The installed Harmony patching backend is intended for the game's Mono runtime. This separate process does not attach to a running game.

Selection regression checks execute the installed game's formation command callback and the original camera no-op guard against managed rendering/selection boundaries. They reproduce the stale contact panel, verify reselection without camera movement, and cover ordinary switching, repeated selections, separate battles, command availability/events, and contained failures. The game's algorithm is not copied into the tests.

These checks do not initialize Unity or Noesis rendering. In-game verification should cover formation selection after selecting an unidentified contact, both with the world-view window open and closed. Actual in-game UI behavior remains unverified by the automated checks.
