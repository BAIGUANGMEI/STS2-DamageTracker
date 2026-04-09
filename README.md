# Damage Tracker

[简体中文](README.zh-CN.md)

Damage Tracker is a Slay the Spire 2 mod that shows a draggable in-combat overlay with per-player damage statistics for the current run.

The overlay is designed to stay readable during multiplayer combat and now supports:

- expanded mode
- compact mode
- side-hidden mode with a small restore tab
- adaptive height with scrolling when the player list grows
- stable display across consecutive runs

Current version: `0.3.0`

## Features

- Track total damage for each player across the current run
- Track damage dealt in the current combat
- Track last hit damage
- Track max hit damage
- Highlight the currently active player
- Show player display name, character name, and portrait when available
- Support drag-and-drop repositioning
- Support expanded, compact, and side-hidden UI states
- Refresh state automatically when combat starts and ends
- Keep the overlay stable across multiple back-to-back runs

## Installation

Download the latest release from the [Releases](https://github.com/BAIGUANGMEI/STS2-DamageTracker/releases) page and extract it into the game's `mods` folder.

Included files:

- `DamageTracker.dll`
- `DamageTracker.pck`
- `DamageTracker.json`

Expected layout:

```text
Slay the Spire 2/
	mods/
		DamageTracker/
			DamageTracker.dll
			DamageTracker.pck
			DamageTracker.json
```

## Usage

- Enter a combat to create the overlay
- Drag the panel with the left mouse button
- Use the top-right toggle button to switch between expanded and compact mode
- Use the side-hide button to collapse the panel to the left or right screen edge
- Click the side tab to restore the panel
- When there are many players, the panel height adapts automatically and the list becomes scrollable

## Damage Tracking Rules

- Direct damage is recorded from the game's actual `DamageResult.UnblockedDamage`
- Poison damage is tracked from actual resolved damage events rather than predicted stack values
- Doom damage is tracked only from the actual Doom kill flow and uses the target's HP before death
- Player ownership is resolved through typed STS2 APIs first, with reflection fallback when needed

## Technical Notes

- Harmony is used to patch STS2 hooks and selected power methods
- The UI is rendered through a Godot `CanvasLayer`
- Run identity is based on `RunState.Rng.StringSeed`
- The tracker seeds known players at run/combat start so the overlay does not depend on who acts first

## Project Structure

- `src/ModEntry.cs`: mod entry point and Harmony patch registration
- `src/RunDamageTrackerService.cs`: damage aggregation and state management
- `src/ReflectionHelpers.cs`: runtime object and player ownership resolution helpers
- `src/DamageTrackerOverlay.cs`: overlay UI
- `DamageTracker.csproj`: project file and game reference configuration
- `mod_manifest.json`: mod manifest
- `project.godot`: Godot project configuration

## Build Requirements

- Windows
- .NET SDK 10
- Godot .NET SDK 4.5.1
- Slay the Spire 2 installed locally

Before building, set the game directory in `DamageTracker.csproj`:

```xml
<Sts2Dir>C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2</Sts2Dir>
```

The project references game assemblies from:

```text
$(Sts2Dir)\data_sts2_windows_x86_64
```

Required assemblies include:

- `sts2.dll`
- `0Harmony.dll`
- `Steamworks.NET.dll`

## Build

Run this in the repository root:

```powershell
dotnet build
```

Successful output:

```text
.godot/mono/temp/bin/Debug/DamageTracker.dll
```

The project is also configured to copy the built DLL into:

```text
Slay the Spire 2\mods\DamageTracker\
```

## Changelog

### v0.3.0

- Fixed missing overlay rows after starting a new run following a previous one
- Improved run and player ownership resolution using typed STS2 APIs where possible
- Fixed character color highlighting issues
- Reworked Poison and Doom tracking to use actual resolved damage flows
- Improved overlay stability with compact mode, side-hide mode, and adaptive height

## Troubleshooting

### Build fails because game DLLs cannot be found

Verify that `Sts2Dir` in `DamageTracker.csproj` points to your actual game installation.

### Build fails because the output DLL is locked

If the game is running, `DamageTracker.dll` may be locked by `SlayTheSpire2.exe`. Close the game and build again.

### The overlay does not appear after a new run starts

Use `0.3.0` or later. The tracker now seeds known players at run/combat start and handles consecutive runs more reliably.

### Player names or portraits are missing

The mod prefers platform/player data from the game runtime and falls back to reflection-based resolution. If the game updates internal fields, `ReflectionHelpers.cs` may need adjustment.

### The panel layout is not ideal for your setup

Adjust sizes, spacing, colors, or behavior in `src/DamageTrackerOverlay.cs`.

## Development Notes

This repository is intended for source learning and mod development practice. Keep source files, project files, and manifests in version control, and avoid committing generated caches or local temporary files.

The repository already ignores typical generated content such as:

- `.godot/`
- `bin/`
- `obj/`
- `.vs/`
- `*.dll`
- `*.pck`

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.

The license applies to the mod source code and custom assets in this repository. It does not apply to the base game, commercial game assets, or third-party restricted content.