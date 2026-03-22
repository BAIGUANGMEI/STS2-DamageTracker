# DamageTracker — Slay the Spire 2 Mod

**Generated:** 2026-03-21
**Stack:** C# / Godot.NET.Sdk 4.5.1 / .NET 10.0 / LangVersion 12.0

## Overview

Damage Tracker mod hooks into STS2 runtime via Harmony, tracks per-player damage across runs/combats, and renders a draggable overlay via Godot `CanvasLayer`.

## Entry Points

| File | Role |
|------|------|
| `ModEntry.cs` | `[ModInitializer]` entry — patches hooks, creates overlay |
| `RunDamageTrackerService.cs` | Core logic — BeginRun, BeginCombat, RecordDamage, state |
| `DamageTrackerOverlay.cs` | Godot `CanvasLayer` UI — subscribes to service events |
| `ReflectionHelpers.cs` | Runtime object resolution (strong types first, reflection fallback) |

## Hook Flow

```
ModEntry.Initialize
  → PatchHook(BeforeCombatStart, AfterCombatEnd, AfterPlayerTurnStart, AfterDamageGiven)
  → DamageTrackerOverlay.EnsureCreated()

HookPatches.BeforeCombatStartPostfix
  → RunDamageTrackerService.BeginRun + BeginCombat
  → DamageTrackerOverlay.ApplyState

HookPatches.AfterDamageGivenPostfix
  → RunDamageTrackerService.RecordDamage
  → DamageTrackerOverlay.Refresh
```

## Structure

```
root/
├── src/                           # C# 源码
│   ├── ModEntry.cs                # Mod init + Harmony patches
│   ├── RunDamageTrackerService.cs  # Damage tracking + state + persistence
│   ├── DamageTrackerOverlay.cs      # Godot CanvasLayer UI
│   ├── ReflectionHelpers.cs        # STS2 object resolution (type-first, reflection fallback)
│   └── *.cs.uid                   # Godot UID 元数据
├── assets/
│   └── localization/               # 本地化资源
│       ├── eng/damage_tracker.json
│       └── zhs/damage_tracker.json
├── project.godot                  # Godot 项目配置
├── DamageTracker.csproj            # .NET 构建配置 (Sts2Dir, post-build copy)
├── mod_manifest.json               # 模组清单
└── .godot/                        # Godot 缓存 (忽略)
```

## Conventions

- **File naming:** `PascalCase.cs` matching the primary class/enum
- **Nullable:** `Nullable=enable` — no `object?` where `T?` is cleaner
- **Reflection fallback:** Strong types (`Player`, `Creature`, `DamageResult`) first; reflection only as fallback when types unavailable
- **Localization:** JSON in `assets/localization/{lang}/damage_tracker.json`; keys are uppercase (TITLE, PLAYER, etc.)
- **EditorConfig:** UTF-8 only (`charset = utf-8`)

## Anti-Patterns (This Project)

- **No CI/CD:** No GitHub Actions, no automated tests — `dotnet build` is the only build cmd
- **Reflection-only paths:** If `ReflectionHelpers` can't resolve a type, it silently returns null/default — relies on game API stability
- **Silent persistence failures:** `SaveState()` swallows all exceptions — no logging on disk write failure

## Build

```powershell
dotnet build
```

Output: `.godot/mono/temp/bin/Debug/DamageTracker.dll`

Post-build: copies DLL to `$(Sts2Dir)\mods\DamageTracker\`

Configure game path in `DamageTracker.csproj`:
```xml
<Sts2Dir>C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2</Sts2Dir>
```

## Notes

- No tests exist in this repo
- The `.cs.uid` files are Godot UID metadata — do not edit manually
- `icon.svg.import` and similar `.import` files are Godot import cache — committed but regenerate on import
