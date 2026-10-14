# Development guide

## Toolchain
- **Godot 4.7.2 .NET** is installed locally in `.engine/godot/` (not versioned). Its export templates are in `%APPDATA%\Godot\export_templates\4.7.2.stable.mono`.
- **.NET SDK 8**.
- **NuGet restore is offline only** (`NuGet.Config`): it uses Godot's bundled packages plus the local package cache.

## Layout
| Path | What |
|---|---|
| `project.godot`, `DestructionRacing.csproj` | Godot project (C#) |
| `src/Rules/` | Pure C# rules library (no Godot): scoring, spins, hit energy, damage, the referee, derby and race standings, championship |
| `tests/Rules.Tests/` | xUnit tests for the rules |
| `src/Core/` | Boot scene, DevTools autoload (command‑line hooks), controls |
| `src/Vehicles/` | The car (`Car.cs`), tuning, keyboard input, the `ImpactSystem` (hit energy from pre‑solve velocities) |
| `src/AI/` | Derby AI (`DerbyBrain`), racing AI (`RaceBrain`), personalities, the PlayerProxy bot |
| `src/Modes/` | `DerbyMode` (the Bowl), `WreckinMode` (Wreckin' Racing), their metrics and sim harnesses (`DerbySim`, `WreckinSim`) |
| `src/Cameras/`, `src/UI/` | Camera rig; the event HUD (`EventHud`, shared by derby and race), the damage diagram, the tuning panel |
| `src/Tools/` | Look-dev scenes: `PhotoStudio` (cars), `BowlLook` and `BowlLookBuilder.gd` (the Bowl), `FogTest` |
| `src/Audio/` | The granular engine (`GranularEngine`) and `EngineTest` |
| `src/Tracks/` | Venues and test scenes: `GreyBoxArena` (grey‑box Bowl), `FigureEightTrack` (grey‑box figure‑8) and `TrackPath` (racing line), `DriveTest`, `DdCodeTest` (tactics T1–T5) |
| `assets/` | Generated cars (`cars/`), shaders (`shaders/`), engine grains (`audio/engines/`), the Bowl's meshes and materials (`bowl/`), CC0 downloads (`third_party/`) |
| `tools/carsmith/` | The car generator (Python + Blender): SDF bodies, panels, detail parts, templates, race-number digits |
| `tools/enginesynth/` | The engine sound synthesizer (Python): grains for `GranularEngine` |
| `scenes/` | Scenes (`boot/`, `derby/`, `race/`, `test/`) |
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
| `tools/dev/headless.ps1 -Scene res://... [-UserArgs @(...)] [-Filter regex]` | Builds, runs a scene headless as fast as possible (one physics step per frame), prints matching lines |
| `tools/dev/bake.ps1 -Scene res://...` | Bakes a scene's LightmapGI unattended |
| `tools/dev/editor.ps1` | Opens the Godot editor |
| `tools/dev/studio.ps1 -Car kitsune [-Presets studio,outdoor] [-Modes normal,zebra] [-Views q34f,side] [-Damage derby]` | Photo-studio renders of a car into `out/studio` |
| `tools/dev/gate_m2.ps1` | The M2 art-gate set: all cars clean, zebra and damaged, plus the Bowl, into `out/gate` |
| `tools/carsmith/build.ps1 <car>` | Generates a car: `assets/cars/<car>/<car>.glb` plus a Blender preview in `out/cars` |
| `python tools/enginesynth/synth.py assets/audio/engines` | Re-synthesizes the engine grains |

## Automated gameplay checks
- **Handling scenarios:** `headless.ps1 -Scene res://scenes/test/DriveTest.tscn -UserArgs '--scenario=accel'` (also `brake`, `circle`, `slalom`, `turnaround`, `keys`).
- **DD Code tactics T1–T5** (plan §4.7, each must succeed in ≥ 60% of trials): `headless.ps1 -Scene res://scenes/test/DdCodeTest.tscn -UserArgs @('--tactic=all','--trials=10') -Filter DDCode`. Add `--verbose` for per-trial notes, `--hits` for spin-assist details, `--tactic=probe` for raw impact measurements.
- **Derby sims** (plan §17.4): `headless.ps1 -Scene res://scenes/test/DerbySim.tscn -UserArgs @('--sims=30') -Filter 'Sim\]'`. The PlayerProxy bot drives the player's car through the keyboard path; the report goes to `out/sims/`. `--profile=skilled`, `--seed=N`, `--dump=60` (print every car's state every 60 s).
- **Wreckin' sims** (plan §17.4): `headless.ps1 -Scene res://scenes/test/WreckinSim.tscn -UserArgs @('--sims=10') -Filter 'Sim\]'`. `--profile=average|racer|hunter|skilled|compare` (`compare` runs the racer and the hunter bot on every seed), `--skill=X`, `--laps=L`, `--cars=N` (`--cars=1` = the bot alone: a clean-lap check), `--dump=S`, `--wallhits`, `--playerhits` (every hit on the player with its source). Each race also prints its damage by source.
- **Watch the bot play:** run `res://scenes/derby/Derby.tscn` or `res://scenes/race/FigureEight.tscn` with `--proxy` (and `--seed=N`). The race also takes `--laps=L`, `--overview[=SIZE]` (top-down view of the layout) and `--view=Chase|ChaseFar|Bumper|InCar`.

## Command‑line hooks (after `--`)
- `--scene=res://...`: the scene to open instead of the default.
- `--capture=<path>` with `--capture-frame=N`: save a screenshot, then quit.
- `--quit-after=N`: smoke test that quits after N frames.
- `--autobake=res://...` (editor only): bake the lightmaps, save the scene, quit.
