# Progress

## M0 Foundation and references — done (2026‑10‑14)
- **Engine:** Godot 4.7.2 .NET and export templates installed (both SHA‑512 verified against the official release).
- **Repo:** git repo initialised (nothing committed yet); offline NuGet config.
- **Rules library** (`src/Rules`): rulesets Standard (DD2 PC) and DD1 Classic (PC), scoring, spin tracker, hit energy, wreck attribution, damage zones and the wreck rule, championship. **58 unit tests passing.**
- **Dev tooling:** build/test/run/capture/bake scripts. Automated screenshots work.
- **Unattended LightmapGI baking works** (`addons/autobake`), so plan §8.1 needs no fallback.
- **Docs:** `docs/reference_board.md` and `docs/fallback_car_models.md`.
- **Not done yet:** the reference feel sheet (needs your OK to watch DD gameplay videos).

## M1b Grey-box Destruction Derby — ready for you to play
- **Play it:** double-click **Play Derby.cmd**. It is also the default scene now; the driving test has its own launcher.
- **The event:**
  - 20 cars start in a ring and meet in the middle; there is a 3‑2‑1 countdown.
  - Spins (90/180/360) and wrecks score DD2 PC points; damaging the points leader (gold beacon) doubles them.
  - The last car running wins and gets the +25 survivor bonus.
  - If you are wrecked: ←/→ watch another car, Space doubles the speed, Enter skips to the results. Tab shows the standings, R restarts from pause or results.
- **Rules core** (`src/Rules/Referee.cs`, `DerbyStandings.cs`):
  - Hits turn into damage per zone and wheel, using the contact point and normal.
  - Front destroyed = wrecked; a car that can't move for 10 s is also wrecked.
  - Spins are attributed to the attacker; wreck credit goes to whoever dealt the most damage.
  - The ×2 leader rule and the event points cap apply.
  - Grinding, scraping and overflow keep damage under control.
  - **81 unit tests.**
- **Physics** (`ImpactSystem`):
  - Hit energy comes from pre-solve velocities; pile-up points are averaged.
  - Spin assist and post-hit grip loss make a tapped car spin.
  - Mechanical damage changes how the car drives: power loss, engine stutter, pull, both sides gone = barely turns, wheel damage and wheel loss.
  - Smoke goes steam → grey → black, then fire; wrecks stay in the arena as obstacles.
  - **DD2 Fast Steer** now really pivots the car at derby speeds.
- **Derby AI** (`src/AI/DerbyBrain.cs`):
  - Personalities: bruiser, racer, vulture, grudge holder, psycho, rookie.
  - Utility targeting weighs distance, exposed sides and rears, finishing wrecks, revenge, the ×2 leader, and the player-attention budget.
  - Behaviours: the opening rush, hunting rear quarters, reverse-ramming with a weak front, the last-second turn (T5), circling around wrecks, wall avoidance and recovery.
  - The anti-stall and final-duel director wakes up passive cars.
- **HUD** (DD2 layout):
  - Speed and Race Points, the big position number, cars still running and the clock.
  - The damage diagram with hit flashes.
  - Spin and wreck popups, a wreck feed, the results table.
- **Checks:**
  - **DD Code T1–T5** scenario tests: 100/100/100/90/100%.
  - **PlayerProxy** bot that drives through the keyboard path, with a **sim harness**: all derby engagement and frustration targets met over 30 derbies (`docs/reference_feel.md`).
  - The M1 handling numbers are unchanged.

## M1 Driving feel — approved (2026‑10‑14: "that feels pretty good")
- **First test build:** you drove it ("ok as a basic concept") and asked for tuning to the videos.
- **Reference feel sheet:** `docs/reference_feel.md`, measured from DD1 and DD2 PC gameplay videos. DD2's speedometer gives absolute speeds.
- **Tuned to it:**
  - Acceleration is DD2-punchy: 0–100 km/h in 2.4 s (DD2 ~2.2 s), 0–160 km/h in 4.7 s (DD2 ~4.6 s), about 230 km/h after 14 s.
  - Chase cameras are low and fixed-distance: **Chase** = DD2 framing (default), **ChaseFar** = DD1 framing (press C).
  - The Bowl floor is grey asphalt, as in DD1.
  - Saved tunings are versioned, so old saves don't override new defaults.
- **Tools:** the circle scenario now holds 60 km/h at full lock; `--view=` sets the starting camera for captures.

| Scenario | Result |
|---|---|
| 0–100 km/h | 2.36 s |
| 0–160 km/h | 4.74 s |
| Braking | 179→0 km/h in 103 m |
| Full lock at 60 km/h | 0.84 g |
| Turn‑around | ≈255° in 2 s |
| Body roll | < 1° |

## M1 Driving feel — first test build
- **Vehicle sim:** arcade raycast vehicle on Jolt. Spring/damper/anti‑roll, Pacejka‑lite tyres with a friction circle, power‑limited drive, light traction control, DD2 brake‑to‑reverse, a dedicated reverse, DD1 turn‑around, DD2 Fast Steer, counter‑steer assist, auto‑righting.
- **Keyboard:** the steering filter, plus 3 presets switched with F3 (Arrows, Classic DD PC, WASD).
- **Cameras:** 4 views (chase far, chase near, bumper, in‑car) and look‑back.
- **Test tools:** HUD, pause, the F1 tuning panel (Save writes `user://car_tuning.json`), and F5 to reset the car.
- **Headless handling scenarios** (`--scenario=accel|brake|circle|slalom|turnaround|keys`). Current values:

| Scenario | Result |
|---|---|
| 0–100 km/h | 7.6 s |
| Braking | 86→0 km/h in 28 m |
| Full‑lock circle | 0.85 g |
| Turn‑around | ≈180° in 1.4 s |
| Body roll | < 1° |
