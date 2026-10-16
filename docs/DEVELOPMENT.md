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
| `src/Vehicles/` | The car (`Car.cs`), tuning, keyboard input, the `ImpactSystem` (hit energy from pre‑solve velocities); the generated model (`CarModel`, `CarSpec` = the rig spec read from the glb) and its damage (`CarDamageVisual` = dent and wear volumes, `CarVisual` = parts, glass, lamps, smoke and fire) |
| `src/AI/` | Derby AI (`DerbyBrain`: behaviours, targeting, the dominant-strategy test bots), racing AI (`RaceBrain`), personalities, the PlayerProxy bot |
| `src/Modes/` | `DerbyMode` (the Bowl) with `DerbySetup` (the event set-up screen) and `DerbyPresenter` (crowd, banners, floodlight flare, juice); `RaceMode` (Wreckin' and Stock Car Racing) with `RaceSetup` (event, layout, direction, light, car, colours, division) and `PitStops` (the pit lane, repair mini-game and autopilot); `TimeTrialMode` with `TimeTrial` (records, the ghost, medal targets); their metrics and sim harnesses (`DerbySim`, `RaceSim`, `TimeTrialSim`) |
| `src/Cameras/`, `src/UI/` | Camera rig and `TvDirector` (TV cameras for spectating and replays); the broadcast HUD (`EventHud`, shared by derby and race, styled by `HudStyle`), the damage diagram, `ScreenFx` (chromatic hit flash, slow-motion grade), the tuning panel |
| `src/Replay/` | `ReplayRecorder` (30 Hz poses, wheels, zone health, hits) and `ReplayPlayer` (playback with TV cameras, slow-mo, seek) |
| `src/Fx/` | Shared effects of a scene: `FxPool` (pooled spark, glass, paint-chip and dust bursts; smoke, flame and scrape-spark emitters), `DebrisPool` (parts that came off, at most 150, resting ones turn static) and `GroundMarks` (ruts, skids, hub gouges and oil painted into a texture the floor shader reads) |
| `src/Tools/` | Look-dev and stress scenes: `PhotoStudio` (cars), `BowlLook` and `BowlLookBuilder.gd` (the Bowl), `SpeedwayLook` and `SpeedwayLookBuilder.gd` (Crossroads Speedway), `FogTest`, `DamageLab` (real hits on a parked car), `PileupTest` (the 20-car stress scene) |
| `src/Audio/` | `AudioMix` (buses, ducking, slow-mo muffle), `SfxLibrary`/`SfxPool`, `CarAudio` (per-car loops), the granular engine (`GranularEngine`), `Announcer` with `DerbyCommentary` and `RaceCommentary`, `MusicPlayer`, `EventAudio` (the sound director of every event, through `IEventSound`), `EngineTest` |
| `src/Tracks/` | Venues and test scenes: `Bowl` (the derby venue: the baked look of a preset, collision, crowd, scoreboard), `Speedway` (Crossroads Speedway: terrain and wall collision, crash cushions, the look of a preset) with `SpeedwayData` (the generated venue data) and `RaceLayout` (a layout and direction: racing line, start, pit lane, surfaces), `Crowd`, `GreyBoxArena` (grey‑box Bowl, still used by the driving test), `TrackPath` (racing line), `DriveTest`, `DdCodeTest` (tactics T1–T5) |
| `assets/` | Generated cars (`cars/`), shaders (`shaders/`), engine grains (`audio/engines/`), synthesized sounds (`audio/sfx/`), announcer lines (`audio/announcer/`), music (`audio/music/`), the HUD font (`fonts/`), the Bowl's meshes and materials (`bowl/`), CC0 downloads (`third_party/`) |
| `tools/carsmith/` | The car generator (Python + Blender): SDF bodies, panels, detail parts, templates, race-number digits |
| `tools/enginesynth/` | The engine sound synthesizer (Python): grains for `GranularEngine` |
| `tools/sfxsynth/` | The sound-effect synthesizer (Python + numpy): crashes, glass, scrapes, tyres, crowd, stings, UI |
| `tools/announcer/` | The announcer script (`script.tsv`) and its generator (Kokoro TTS + the stadium PA chain) |
| `tools/tracksmith/` | The Crossroads Speedway generator (Python + shapely): both layouts, banking, pit lanes, walls and barriers, the terrain and surface rasters |
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
| `tools/dev/lab.ps1 [-Model brougham] [-Out out/lab] [-NoBuild]` | The scripted damage lab: rams from every side, battered, smoking, wrecked and burning, wheel off, scraped, rolled; 14 shots and a contact sheet into `out/lab`. The window must stay visible: a minimised or covered window draws no frames (the script retries once if shots are missing) |
| `python tools/dev/contact_sheet.py <out.png> <images...> [--cols 3]` | Tiles screenshots into one labelled sheet |
| `python tools/dev/derby_report.py out/sims/derby_*.json [--label X]` | Aggregates several derby sim batches into one report (the plan §17.4 targets) |
| `Godot --headless -s res://src/Tools/BowlLookBuilder.gd -- --preset=afternoon\|golden\|night` | Rebuilds a Bowl preset scene; then `bake.ps1 -Scene res://scenes/bowl/BowlLook[_Golden\|_Night].tscn` |
| `python tools/tracksmith/crossroads.py` | Regenerates the Speedway data (`assets/speedway/crossroads*.{json,bin}`) and a top-down preview in `out/speedway` |
| `Godot --headless -s res://src/Tools/SpeedwayLookBuilder.gd -- --preset=afternoon\|golden\|night [--rebuild-meshes]` | Rebuilds a Speedway preset scene (`--rebuild-meshes` also regenerates the meshes and their lightmap UVs: then rebake all three); then `bake.ps1 -Scene res://scenes/speedway/SpeedwayLook[_Golden\|_Night].tscn`. The scene itself opens a look-dev viewer (V next view, P photo); `--shots=out/x` saves every view and quits |
| `python tools/sfxsynth/synth.py` | Re-synthesizes the sound effects into `assets/audio/sfx/` |
| `.engine/kokoro-venv/Scripts/python.exe tools/announcer/generate.py` | Generates the announcer lines (incremental; offline once the Kokoro model is cached) |
| `tools/carsmith/build.ps1 <car> [-NoGlb] [-Views q34f,side,q34r,front]` | Generates a car: `assets/cars/<car>/<car>.glb` plus a Blender preview in `out/cars`. The nine roster cars are `tools/carsmith/cars/*.py`; all but the Kitsune are morphs of the template (`templates.py`: sedan, coupe, hatch, wagon). Blender's traceback is hidden by the script's filter: run Blender directly to see it |
| `python tools/enginesynth/synth.py assets/audio/engines [car,car,...]` | Re-synthesizes the engine grains (all, or the named cars) |
| `python tools/liverysmith/sponsors.py` | Redraws the fictional sponsor decals, `assets/textures/sponsors.png` (2 × 4 cells; the cell is `Livery.Sponsor`) |
| `bash tools/dev/balance.sh [events=200] [parallel=8] [tag=m5] [extra args]` | Car balance (plan §9.3): N derbies, N Wreckin' races and N Stock Car races with `--randomcars` in parallel headless processes (a fifth of them for the derbies), then `balance_report.py`; logs and `balance_<tag>.md` in `out/sims`. 120 of each at 16 processes take about 25 minutes |
| `bash tools/dev/race_targets.sh [races=8] [processes=4] [tag=m6] [extra args]` | Race sims against the plan §17.4 targets: Wreckin' and Stock Car on the oval and the figure-8, each over P parallel processes, merged per configuration into `out/sims/<tag>_<kind>_<layout>.md`. About 100 CPU-s per race and one core per process: 16 processes do 9–10 races a minute on this machine |
| `python tools/dev/race_report.py <json...> [--stockcar] [--crossover] [--label X] [--md out.md]` | Merges race sim batches (their `.json`) into one report |
| `bash tools/dev/endurance.sh [laps=100]` | The endurance check (plan §17.1): one 100-lap race (8 cars, no damage) per layout and direction; prints laps, stuck resets and the longest stop of every car, then OK or FAILED |
| `python tools/dev/balance_report.py <logs...> [--md out.md]` | Per-car results from the `[CarResult]` lines: mean points, place, wins, survival or finish per mode, the ±15% check in each car's intended mode(s), the top 3 in every mode, and the car-select verdicts |

## Automated gameplay checks
- **Handling scenarios:** `headless.ps1 -Scene res://scenes/test/DriveTest.tscn -UserArgs '--scenario=accel'` (also `brake`, `circle`, `slalom`, `turnaround`, `keys`).
- **DD Code tactics T1–T5** (plan §4.7, each must succeed in ≥ 60% of trials): `headless.ps1 -Scene res://scenes/test/DdCodeTest.tscn -UserArgs @('--tactic=all','--trials=10') -Filter DDCode`. Add `--verbose` for per-trial notes, `--hits` for spin-assist details, `--tactic=probe` for raw impact measurements.
- **Derby sims** (plan §17.4): `headless.ps1 -Scene res://scenes/test/DerbySim.tscn -UserArgs @('--sims=30') -Filter 'Sim\]'`. The PlayerProxy bot drives the player's car through the keyboard path; the report goes to `out/sims/`. `--profile=skilled`, `--seed=N`, `--dump=60` (print every car's state every 60 s), `--division=1..4`, `--strategy=hide|ram|reverse|farm` (a dominant-strategy test bot in the player's car; each derby then prints the bot's final damage). Experiments: `--nosqueeze`, `--nocover`, `--greywalls` (the M1–M3 wall), `--reartough=X`, `--opening=SCALE,SECONDS` (the opening damage ramp), `--attackshare=X`. Each early player wreck (≤ 60 s) prints the car's state and where its front damage came from. Run batches in parallel processes with different `--seed` ranges and merge them with `derby_report.py`.
- **Race sims** (plan §17.4): `headless.ps1 -Scene res://scenes/test/RaceSim.tscn -UserArgs @('--sims=10') -Filter 'Sim\]'`. `--kind=wreckin|stockcar`, `--layout=figure8|oval`, `--reverse`, `--profile=average|racer|hunter|skilled|compare` (`compare` runs the racer and the hunter bot on every seed), `--skill=X`, `--laps=L`, `--cars=N` (`--cars=1` = the bot alone: a clean-lap check), `--dump=S`, `--wallhits`, `--playerhits` (every hit on the player with its source), `--pitlog` (every pit decision, stop and release), `--pittest` (the bot stops on its first pass). Each race (and each derby) also prints its damage by source and the DD Code used in the field (brake-checks and the tailgaters they caught, squeezes, tailgating, dodges, blocks).
- **Race experiments** (RaceSim): `--nodefence`, `--nolag`, `--notailgate`, `--nobrakecheck`, `--nosqueeze` switch a DD Code behaviour off; `--nobarrels` drops the cushions' barrel colliders; `--scheadway=S`, `--scdraft=X`, `--scpace=X`, `--scbumplap=N` (Stock Car following gap, extra drafting, AI pace, the lap the bumping starts); `--leaderhunt=BEHIND,AHEAD` (metres); `--proxypitline=P`; `--wallhits` marks hits on a cushion; `--outdir=out/sims/x` (where the report goes); `--endurance` (the 100-lap check).
- **Time Trial targets:** `headless.ps1 -Scene res://scenes/test/TimeTrialSim.tscn -UserArgs @('--layout=oval','--sims=5')` drives the skilled and the average bot round every car and checks the medals (gold reachable by "skilled", not by "average"); `--reverse`, `--car=<id>`, `--laps=L`, `--writetargets` (merges the targets of the cars it ran into `assets/speedway/time_trial/<layout>_<dir>.json`, so `--car=<id>` updates one car).
- **Reproducible:** the same command gives the same results (the impact system sorts Jolt's contacts). A seed run alone plays out differently from the same seed inside a batch, because Jolt's history differs; replay a batch with `--sims=N` up to that seed.
- **Model cars vs the grey box:** every scene uses the generated cars (the nine of `CarCatalog`; their size, wheel positions and collider come from the glb). `--greybox` turns every car back into the M1 grey box, for comparing tuning against the M1 baselines; the DD Code test takes `--model=<car>` for both cars.
- **Per-car handling** (M5): each model's mass, power, drag, grip, steering and toughness come from its five ratings (`CarInfo.Tuning`, `CarInfo.DamageToughness`). `--sametuning` gives every car the shared baseline and toughness 1 (A/B runs). `--car=<id>` picks the player's (or the proxy's) model.
- **Car balance** (plan §9.3): `--randomcars` (derby and race, the sims too) puts every AI driver and the proxy in a random model, so each car meets every archetype. Every headless event prints one `[CarResult]` line per car (`--carresults` adds them to a windowed run); `balance.sh` and `balance_report.py` turn them into the report. `--ratingscale=mass:X,tough:Y,...` (keys mass, accel, top, grip, steer, tough, and fwdfront and fwdrear for the front-drive grip) overrides the balance knobs of `RatingScale` for tuning runs; `--carstats=volant:7,7,8,3,5` (several separated by `;`) sets a car's speed, acceleration, handling, weight and toughness. In races the `[CarResult]` line also gives `spun` (times the car was spun after a hit), `stuck` (put back on the track) and `longeststop`.
- **Driver taunts:** `--taunts=off|some|all` sets how often drivers talk on the radio (default some); `--radiotest` shows one banner during the countdown, for captures (use `--fixed-fps 60` so the capture frame lands inside its four seconds).
- **Pileup stress test** (plan §16, at every milestone): `headless.ps1 -Scene res://scenes/test/PileupTest.tscn -Filter Pileup` checks Jolt stability (no launches, nothing through the floor, no jitter once settled, no cars inside each other) and the cost per physics tick. With rendering, effects and debris: `--visual --perf=2 --quit` (windowed).
- **Watch the bot play:** run `res://scenes/derby/Derby.tscn` or `res://scenes/race/Race.tscn` with `--proxy` (and `--seed=N`). The race also takes `--kind=wreckin|stockcar`, `--layout=figure8|oval`, `--reverse`, `--laps=L`, `--overview[=SIZE]` (top-down view of the layout) and `--view=Chase|ChaseFar|Bumper|InCar`; `res://scenes/race/TimeTrial.tscn` takes `--layout`, `--reverse`, `--car` and `--proxy`. `--speedwaycheck` prints the layout's lap length and pit lane.

## Command‑line hooks (after `--`)
- `--scene=res://...`: the scene to open instead of the default.
- `--capture=<path>` with `--capture-frame=N`: save a screenshot, then quit.
- `--quit-after=N`: smoke test that quits after N frames.
- `--perf[=S]`: print frame time (mean and 1% low), draw calls, triangles and VRAM every S seconds (default 5).
- `--shot-every=S --shot-dir=out/x`: save a screenshot every S seconds (stress scenes).
- `--greybox`: every car is the grey box (see above).
- `--maxcontacts=N`: contacts each car reports to the impact system (default 48; for experiments).
- `--pinned=S` (derby): how long a car may rock in place before it counts as immobilised (default 20 s; for experiments).
- `--preset=afternoon|golden|night` (derby, race, Time Trial): the venue's light; `--division=1..4`; `--car=<id>` (one of the nine).
- The set-up screens: `res://scenes/race/RaceSetup.tscn` (races and Time Trial) and the derby's; the launchers start them. Starting a mode scene directly skips the screen and takes the hooks above.
- `--autoskip` (derby, windowed): fast-simulate to the results; `--autoreplay`: open the replay at the results (replay checks).
- `--audiolog` (derby): print what the audio loaded and every announcer line as it is said.
- `--marks-debug` (derby): show the raw ground-marks texture on the floor; `--marks-dump=out/x.png`: save it when the derby closes.
- `--autobake=res://...` (editor only): bake the lightmaps, save the scene, quit. It waits until that scene is the one open (the editor may first restore the last session's scene).
- `--loadtime` (race): prints the time to the first frame and to the green light, after launch or after an R restart; `--loadtime=restart` presses R itself once and quits after the reloaded race starts.
- `--fliptest` (race): puts the player's car on its roof 4 s after GO (the rollover and the righting).
- `--pittest` (race with `--proxy`): the bot stops on its first pass.
