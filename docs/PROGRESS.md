# Progress

## M0 Foundation and references — done (2026‑10‑14)
- **Engine:** Godot 4.7.2 .NET and export templates installed (both SHA‑512 verified against the official release).
- **Repo:** git repo initialised (nothing committed yet); offline NuGet config.
- **Rules library** (`src/Rules`): rulesets Standard (DD2 PC) and DD1 Classic (PC), scoring, spin tracker, hit energy, wreck attribution, damage zones and the wreck rule, championship. **58 unit tests passing.**
- **Dev tooling:** build/test/run/capture/bake scripts. Automated screenshots work.
- **Unattended LightmapGI baking works** (`addons/autobake`), so plan §8.1 needs no fallback.
- **Docs:** `docs/reference_board.md` and `docs/fallback_car_models.md`.
- **Not done yet:** the reference feel sheet (needs your OK to watch DD gameplay videos).

## M1c Grey-box Wreckin' Racing — ready for you to play
- **Play it:** double-click **Play Wreckin.cmd**. (The derby is **Play Derby.cmd**; **Play Driving Test.cmd** is the empty-arena driving test.)
- **The track:** a grey-box Crossroads Speedway figure-8 (`src/Tracks/FigureEightTrack.cs`).
  - Two 70 m turns, joined by straights that cross at grade in the middle: DD1's blind crossover.
  - 13 m wide (three cars abreast), 984 m per lap, laps of about 37–40 s (plan: 35–55 s).
  - The walls open where the legs cross. Where a wall resumes it is flared, and the corners are closed.
- **The race:**
  - 20 cars on a 3-wide grid, you start 13th; a 3-2-1 countdown, 6 laps.
  - Spins and wrecks score as in the derby. Hitting the car in 1st (gold **LEADER ×2** beacon) doubles the points.
  - Finishing bonus 25/15/10 for the top three. A wreck is a DNF, and the wreck stays on the track as an obstacle.
  - HUD: your race position (big number), lap, LAP and FINAL LAP banners, WRONG WAY, Tab = running order, damage diagram.
  - Wrecked: ←/→ watch another car, Space ×2 speed, Enter skip to the results.
- **Racing AI** (`src/AI/RaceBrain.cs`):
  - Three lanes with pure pursuit, and a curvature speed profile by skill.
  - Car-following, overtaking into clear lanes only, the inside line for good drivers.
  - Shoving past wrecks, and the turn-around after a spin.
  - **Crossover by personality:** cautious drivers yield, bruisers floor it, psychos aim for a T-bone.
  - **DD Code on offence:** T2 (turn into the rear quarter of a car beside you), T3 (tap a car braking for a bend), and the leader-killer (opportunists hunt the ×2 leader).
- **Shared code:** the derby and the race share one HUD (`EventHud`), the referee and the impact system. The rules library gained race standings (laps, finishing order, DNFs): **84 unit tests**.
- **Calibrations ✱** (plan §20.2, A21–A24):
  - Race damage ×0.5. At full damage the player was wrecked before half distance in almost every race.
  - AI pace ×0.97 (Division 4), so racing for the finishing bonus pays as well as hunting.
  - The flared crossover walls, and the race end when you finish (or when the winner finishes, if you are wrecked).
- **Checks:**
  - A Wreckin' sim harness (`WreckinSim`), 20 races: all plan §17.4 Wreckin' targets are met. Results in `docs/reference_feel.md`.
  - DD Code tactics T1–T5: 10/10 each. 84 unit tests pass.
- **Sims are now reproducible.** The engine stutter of damaged cars used Godot's global random generator, so every launch differed.
  - Re-measured, the derby's first minute was too harsh: the opening crunch is now ×0.5 over 75 s (was ×0.6 over 60 s).
  - One derby target is still missed: 55% of points from deliberate hits (target ≥ 70%). It is for your derby playtest.

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
  - **PlayerProxy** bot that drives through the keyboard path, with a **sim harness** (`docs/reference_feel.md`). Re-measured in M1c with reproducible sims over 60 derbies: every target is met except the share of points from deliberate hits (55%, target ≥ 70%).
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
