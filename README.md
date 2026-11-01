# Bug Fixes by MebiusTos

A collection of bug fixes for Sea Power, loaded through Anchor Chain. Current version: **0.1.4**.

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

### CeaseFirePreservesWeaponsHold

Preserves each unit's **Weapons Hold** setting when issuing **Cease Fire** from the unit context menu. Units on Weapons Free still change to Weapons Tight, and units already on Weapons Tight remain there. If the game's formation-control settings propagate the change to other units, each unit already on Weapons Hold keeps that setting independently.

The game's documented Cease Fire behavior requests Weapons Tight even when the unit was on Weapons Hold. This fix intentionally changes that part of the command while preserving the native stop action and formation routing. Explicit manual changes from Weapons Hold to Weapons Tight or Weapons Free remain available.

Protection applies only during the context-menu command's weapon-status update; no per-frame monitoring is added. Keyboard cease-fire handling and formation stop/recall commands keep their native behavior.

To reproduce the original issue with this fix disabled, set a friendly unit to Weapons Hold and choose **Engage with > Cease Fire** in its context menu. The unit changes to Weapons Tight. With the fix enabled, it stays on Weapons Hold while the stop command still executes.

### ContextMenuInputRecovery

Fixes 3D mouse controls remaining blocked after closing the battle context menu, for example after choosing **Cease Fire** while paused. Rotation, wheel zoom and right-clicking a unit in the 3D view can stop working until the map is toggled or time resumes.

The final UI-hover check can still hit the closing menu item. The menu disappears without another hover update, leaving the game treating the mouse as over UI. This fix observes the shared battle menu's Closed event and schedules one refresh after dismissal using the game's existing UI-hover behavior. The refresh uses the current pointer position and preserves the native handling of other UI and the PiP view.

There is no continuous polling. A reopened menu, a hidden/replaced battle view or a destroyed binding prevents the queued refresh from changing input. The fix binds when the game assigns its completed UI root, and obtains a behavior instance already created by the game's XAML. It does not patch or initialize the custom Noesis behavior type during startup.

To reproduce the original issue with this fix disabled, pause while viewing a friendly ship, choose **Cease Fire** from its right-click menu, and try rotation and wheel zoom. With the fix enabled, mouse controls should remain available after the menu disappears, while controls remain restricted when the pointer is over another UI element.

## Disabling individual fixes

All fixes are enabled by default and can be disabled independently. In the installed mod folder (the folder containing `MebiusTosBugFixes.dll`), copy `fixes.ini.example` to `fixes.ini` and set the fix you want to disable to `false`.

For example, this configuration keeps the formation-manager and context-menu input fixes enabled and disables the cease-fire ROE change:

```ini
[Fixes]
FormationUnitReselection = true
CeaseFirePreservesWeaponsHold = false
ContextMenuInputRecovery = true
```

Set a fix back to `true` to re-enable it. If `fixes.ini` is absent, all fixes are enabled; omitted entries are also enabled. **Restart Sea Power after changing these settings.** Preserve your `fixes.ini` when updating the mod.

Unknown fix IDs and invalid Boolean values produce a warning at startup, and the affected entries are ignored. This includes settings left behind by removed fixes. Ignoring an invalid value preserves the fix's default (enabled) or its previous valid value in the file; other fixes continue loading. Duplicate valid entries for a known fix and malformed configuration syntax are logged as configuration errors and stop initialization. The input file is not overwritten.

Disabling the package in the game's mod manager prevents all of its fixes from being installed, regardless of the individual settings.

## Project structure and extension

- `src/Plugin.cs`: Anchor Chain entry point, package activation check, and configuration loading.
- `src/BugFixes.cs`: installation and rollback for individual fix modules, each with its own Harmony owner ID.
- `src/Fixes/Selection/`: formation-command selection synchronization and contained command forwarding.
- `src/Fixes/Combat/`: scoped cease-fire weapon-status protection with native formation routing.
- `src/Fixes/Input/`: event-driven context-menu input recovery with native hover recalculation.
- `tests/`: regression checks for settings, activation, selection, cease-fire behavior, deferred input recovery, and the installed game's method instructions.
- `assets/workshop/workshop_icon.jpg`: the Steam Workshop icon, preserved as the supplied JPEG without recompression.

Add future fixes as independent `IBugFix` implementations and register them in `Plugin`. If a fix fails to install, the framework logs the error, removes that fix's patches, and continues with the other fixes.

Exceptions from initialization, patch installation, selection-command synchronization, cease-fire status updates, context-menu binding/refresh/cleanup, and logging are contained within this mod instead of propagating to Anchor Chain, another mod's Harmony call, or the game's update loop.

## Building and verification

Use the Windows .NET SDK. The test runner targets .NET 10, and the plugin targets .NET Standard 2.1. Copy `Directory.Build.local.props.example` to `Directory.Build.local.props` and set the game and Anchor Chain paths. No NuGet downloads are required.

```powershell
.\build.ps1 -Test
.\build.ps1 -Test -InstallLocal
```

`-Test` runs configuration, activation, selection, cease-fire and deferred context-menu input regression checks on .NET. It also invokes a separate process using the game's bundled Mono runtime to run those checks and verify Harmony patch installation and removal against the actual formation-command setter, cease-fire command/status setter, and battle UI-root assignment setter. The installed Harmony patching backend is intended for the game's Mono runtime. This separate process does not attach to a running game.

Selection regression checks execute the installed game's formation command callback and the original camera no-op guard against managed rendering/selection boundaries. They reproduce the stale contact panel, verify reselection without camera movement, and cover ordinary switching, repeated selections, separate battles, command availability/events, and contained failures. The game's algorithm is not copied into the tests.

Cease-fire regression checks execute the installed game's menu callback and weapon-status setter against managed unit/formation boundaries. They cover mixed settings across native formation-control modes, normal stop arguments, repeated commands, explicit manual changes, scoped failure handling, and actual status-write interception on game Mono. In the standalone Mono process, only Unity profiler-marker creation and metadata initialization are replaced with inert test callbacks; the tested game methods and installed DLL files are unchanged.

Menu-input regression checks reproduce the stale-input gate at a simulated native hit-test boundary, verify deferred refresh with the current pointer, preserve input restrictions over other UI, and cover reopened menus, hidden/replaced/disposed views, coalesced notifications, one-shot scheduling and contained failures. The actual battle root setter is also patched and invoked with a cleared root on standalone game Mono; no custom Noesis behavior is initialized. Live Noesis event binding, view teardown and paused gameplay require an in-game check.

These checks do not initialize Unity or Noesis rendering. In-game verification should cover formation selection after selecting an unidentified contact, both with the world-view window open and closed. Also verify Cease Fire on individual units and mixed-ROE formations, then confirm that manual ROE changes remain available. Also test paused right-click Cease Fire followed by rotation, zoom and a 3D unit menu without needing Tab; verify ordinary menu actions and input restrictions over other UI. Actual in-game UI behavior remains unverified by the automated checks.
