# Development guide

## Toolchain
- **Godot 4.7.2 .NET** is installed locally in `.engine/godot/` (not versioned). Its export templates are in `%APPDATA%\Godot\export_templates\4.7.2.stable.mono`.
- **.NET SDK 8**.
- **NuGet restore is offline only** (`NuGet.Config`): it uses Godot's bundled packages plus the local package cache.

## Layout
| Path | What |
|---|---|
| `project.godot`, `DestructionRacing.csproj` | Godot project (C#) |
| `src/Rules/` | Pure C# rules library (no Godot): scoring, spins, hit energy, damage, championship |
| `tests/Rules.Tests/` | xUnit tests for the rules |
| `src/Core/` | Boot scene, DevTools autoload (command‑line hooks) |
| `src/Tracks/` | Venues; `GreyBoxArena` is the grey‑box Bowl |
| `scenes/` | Scenes (`boot/`, `test/`) |
| `addons/autobake/` | Editor plugin for unattended lightmap baking |
| `tools/dev/` | Dev scripts (below) |
| `out/` | Screenshots and logs (not versioned) |

## Dev scripts (PowerShell)
| Script | Does |
|---|---|
| `tools/dev/build.ps1` | Builds the game, the rules library and the tests |
| `tools/dev/test.ps1` | Runs the rules unit tests |
| `tools/dev/import.ps1` | Headless resource import (creates `.godot/`) |
| `tools/dev/run.ps1 [-Scene res://...]` | Builds and plays |
| `tools/dev/capture.ps1 -Out out/shots/x.png [-Scene ...] [-Frame 120]` | Builds, runs windowed, saves a screenshot, quits |
| `tools/dev/bake.ps1 -Scene res://...` | Bakes a scene's LightmapGI unattended |
| `tools/dev/editor.ps1` | Opens the Godot editor |

## Command‑line hooks (after `--`)
- `--scene=res://...`: the scene to open instead of the default.
- `--capture=<path>` with `--capture-frame=N`: save a screenshot, then quit.
- `--quit-after=N`: smoke test that quits after N frames.
- `--autobake=res://...` (editor only): bake the lightmaps, save the scene, quit.
