# M8 checklists: performance and beauty (plan §16, §17.3)

The M8 acceptance (plan §18) is "performance and beauty checklists pass". These are the results at the end of M8 (19 Oct 2026). ✅ = met, ☑️ = met as far as stills and logs can show (judge it in motion in your playtest), ⚠️ = met with an exception named in the row, ❌ = missed (a deviation, ✱ = its A-number in plan §20.2), ⏳ = can't be measured here.

## How it was measured

- **The dev PC:** RTX 5090, Ryzen 7 7800X3D, Godot 4.7.2 Forward+ (Vulkan), Jolt at 60 Hz.
- **4K Ultra:** a 2560×1440 window at render scale 1.5 (3840×2160 rendered; `--resolution 2560x1440 -- --scale3d=1.5`). The Ultra preset: shadows Ultra (8192 atlases, soft shadows high), MSAA ×4 + SMAA, SSAO + SSIL, glow, the night haze (volumetric fog), detail high, film look, motion blur 40%, vsync off.
- **The runs:** real-time events with 20 cars. The player's car is driven by the AI proxy (`--proxy`), seen through the chase camera, then through the TV cameras once it is out. `--perf=10` prints a line every 10 s: the mean fps and 1% low of that window, the GPU time, physics, AI, deformation, particles and VRAM. The logs are in `out/m8/perf/`.
- **Finding the GPU costs:** `--ab=...` turns one feature off every other window, on a fixed 30 fps game clock so every run plays out the same (`docs/DEVELOPMENT.md`).

## Performance (plan §16)

### Frame rate, 4K Ultra (target ≥ 120 fps)

| Scene | Mean fps per 10 s window | 1% lows | |
|---|---|---|---|
| Derby, afternoon (seed 4) | 140–263 | 92–186 | ✅ |
| Derby, night (seed 3) | 144–203 | 83–171 | ✅ |
| Derby, night (seed 4) | 103–202; 18 of 19 windows ≥ 120 | 85–171 | ⚠️ one window at 103 |
| Race, figure-8, afternoon | 262–311 | 189–280 | ✅ |
| Race, oval, night | 146–227 | 110–168 | ✅ |
| Pileup stress scene, 20 cars | 193–206 | 80–197 | ✅ |

The races were measured before the smoke fix below, which only made them cheaper.

**What M8 changed to get here:**

- **The smoke.** Late in a night derby the GPU took 9–14 ms (72–110 fps). Turning the smoke off saved 4–6 ms; nothing else came close.
  - Measured on the fixed clock in the heaviest stretch, the smoke went from 4.8 ms to 2.0 ms.
  - Its light is computed at a puff's four corners, not at every pixel of every layer: −1.8 ms.
  - The empty fringe of each puff is skipped (`discard`): −1.0 ms.
  - Its billow noise is baked into a texture, not computed with 16 `sin` hashes a pixel: about −0.1 ms. The noise was never the cost.
  - Side by side, the smoke looks the same (`out/m8/smoke/compare.jpg`).
- **Wrecks burn, then smoulder,** as the code always meant:
  - A wreck burns for 9 s, or 25 s if its engine bay was already gone. It used to burn for good below 8% front health, and late in a derby a field of ten fires and their lights cost about 3 ms.
  - After the fire, a wreck's plume thins over 20 s to half its puffs.
  - Fire lights fade out beyond 30 m from the camera.
- **Draw calls** (earlier in M8): only the body, panels, bumpers, tyres and rims cast shadows. The small parts are hidden beyond 70 m: further through a long lens, since the range scales with the camera's zoom. A night race went from 95–156 to 133–198 fps.

### Budgets

| Budget | Measured | |
|---|---|---|
| Physics ≤ 3.5 ms | Mean tick 0.57–0.78 ms (20 cars, headless). In a 4K derby, the worst tick of each second averages 1–4.6 ms; the single worst ticks (20–33 ms) come at load. | ✅ |
| AI ≤ 1 ms | 0.02–0.17 ms a tick (all 19 drivers) | ✅ |
| Deformation ≤ 0.5 ms (amortised) | 0.00–0.22 ms a frame (dents, then volume encode and upload) | ✅ |
| Cars: LOD0 ≤ 60k, LOD1 15k, LOD2 4k triangles | LOD0 283k–400k; Godot's generated LODs go down to 12k–17k (`--lodreport`) | ❌ ✱ A30 |
| ≤ 6 shadowed lights | Day: the sun. Night: the 10 floodlights of the M6 look. | ❌ ✱ A31 |
| ≤ 40k particles | At most 2,928 emitting | ✅ |
| ≤ 6 GB VRAM | 2.0–2.7 GB at 4K Ultra | ✅ |
| Stress scene: the 20-car pileup, every milestone | Passes, seeds 1–12 headless and the visual runs | ✅ |
| No GC hitches | The pileup's .NET GC pauses went from 103–141 ms to 5 ms; no tick gap over 30 ms | ✅ |

The CPU budgets are written for a mid-range PC. The 7800X3D is faster, but on a CPU half as fast the mean physics tick and the AI would still be well inside them.

### Other hardware

| Target | |
|---|---|
| Mid-range (RTX 3060 / RX 6600), 1080p High ≥ 60 fps, 1% lows ≥ 50 | ⏳ can't be measured on this PC (needs testers in M9) |
| Minimum (GTX 1660), 1080p Low + FSR ≥ 60 fps | ⏳ as above |

At 4K Ultra the dev PC spends 4–9 ms of GPU time a frame. 1080p High draws a quarter of the pixels, with MSAA ×2 instead of ×4 and smaller shadow maps. That suggests the mid-range target is within reach, but it has not been measured.

## Beauty (plan §17.3)

| Check | Evidence | |
|---|---|---|
| No placeholder art | Every car, venue, menu and HUD element is final art; the grey-box cars appear only with the dev switch `--greybox` | ✅ |
| No z-fighting | None in the hero shots, the derby clip frames and the night and afternoon series | ☑️ |
| No LOD popping | Godot switches the auto-LODs by screen size. The small parts' 70 m range now scales with the lens, so the TV long shots keep them. Popping only shows in motion. | ☑️ |
| No fog ghosting | Flickering lights (fire, flashbulbs) have fog energy 0 (plan §8.1); no trails in the night series | ☑️ |
| No surface wobble in zebra renders | M2–M5 gate renders (`out/gate/<car>/clean/*_zebra_*.png`); no car mesh changed since | ✅ |
| A readable HUD at 1080p | The 1080p night series (`out/m8/smoke/`): position, points, damage diagram, feed and banners all read | ✅ |
| Hero shots read as DD: the Bowl by the mountains on a sunny afternoon | `out/m8/hero/bowl_hero.png`, `bowl_stands.png` (and golden hour, night) | ✅ |
| … a smoky pileup | `out/m8/derby_clip.webp`, `out/m8/smoke/new_afternoon/` | ✅ |
| … a battered car with a big number and a division roof | `out/m8/hero/`, the clip frames | ✅ |
| Damage looks like metal, not rubber | Approved at the M2 and M3 gates; M8 adds rust from the livery's age (`out/m8/rust_sheet.png`) | ✅ |

**Defects found and fixed in M8:**

- **The TV cameras shot through the catch fence.** The low trackside posts stood 12 cm behind the chain-link, and the long lens blew each 6.5 cm diamond up to a quarter of the frame. They now stand on the wall cap, inside the fence. The fence also fades into its average cover within 2.5 m of a camera.
- **The leader's beacon** filled a third of the frame when it passed a camera a few metres away. Its on-screen size is now capped inside 14 m.
- **"WRECKED – RACE OVER" in the Bowl.** A wrecked player now reads "WRECKED – YOU'RE OUT" in a derby and "WRECKED – DNF" in a race.
