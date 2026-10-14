# Reference feel sheet (DD1 / DD2 PC)

The numbers below were measured from PC gameplay videos on YouTube, watched without logging in. Frames were sampled into contact sheets at known timestamps, so timings are accurate to about ±0.25 s. Screen positions are fractions of the 4:3 game picture, measured from the top-left corner.

## Sources

| Game | Video | Used for |
|---|---|---|
| DD1 PC | `7lXYNOxPwMU` (Destruction Derby, The Bowl) | Derby timing, Bowl look, DD1 chase camera |
| DD1 PC | `xWVgPW6Uiyk` (Speedway race) | Lap times, race length, HUD, spins |
| DD2 PC | `J8sM4LYVueU` (DD2 1996 PC gameplay, Win 10) | Speedometer readings, acceleration, DD2 chase camera, lap times, HUD |

## Measurements

### Speed and acceleration (DD2 PC speedometer, standing start at about 1033.2 s)

| Time after GO | 0.3 s | 0.8 s | 1.3 s | 1.8 s | 2.3 s | 2.8 s | 3.3 s | 3.8 s | 4.3 s | 4.8 s | 5.3 s | 5.8 s |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| km/h | 19 | 44 | 65 | 85 | 103 | 118 | 131 | 143 | 154 | 165 | 175 | 181 |

- **0–100 km/h:** about 2.2 s. **0–160 km/h:** about 4.6 s. The launch is about 1.3–1.4 g, which is pure arcade.
- **Top speed:** about 240 km/h was seen on a long straight (242 KPH). Mid-corner speeds are typically 150–200 km/h.
- **Bowl cross-check (DD1):** from GO to the first centre pile-up takes about 2.5 s. Over about 35 m, that also implies about 1.1 g of launch acceleration. Both games therefore use the same punchy launch.

### Chase camera framing

| | DD1 PC (default far view) | DD2 PC (default view) |
|---|---|---|
| Car width | ~19% of picture width | ~31% |
| Rear bumper (bottom) | ~72% down | ~89% down |
| Roof | n/a | ~58% down |
| Horizon | ~46% down (looks slightly down) | ~50% (level) |
| Derived camera | ~9 m behind the car centre, ~2 m up, ~2.5° down, vertical FOV ~55° | ~6.5 m behind, ~2 m up, level, vertical FOV ~55° |

- Both cameras follow the car body's heading, not its velocity, and stay at a fixed distance at any speed.

### Races and derbies

| | DD1 PC | DD2 PC |
|---|---|---|
| Field | 16 cars | 20 cars |
| Race length | 12 laps, Speedway lap ~15 s, race ~3.5 min | 5, 8 or 10 laps; on the 8-lap circuit with grandstands a lap is ~37.5 s, race ~5 min |
| Derby (Bowl) | ~60 s from GO to "Race Over" | n/a in this video |
| HUD | "Race Points" top left, damage diagram bottom right, "Lap no. xx/12" top right, big position "xx/16" bottom left, "1ST" marker over the leader | Speed (KPH) and Race Points top left, big position in the centre, "20 Still Running" and "Lap No. 01/08" top right, damage car diagram on the right |

### Behaviour

- **Spins:** a rear-quarter tap spins the target within about 0.5 s (DD1 Speedway).
- **Look:** DD1's Bowl has a grey asphalt floor, low concrete walls with a curved banked section, a grandstand, green mountains behind it, and a blue sky with clouds.

## Targets and current M1 values

| Item | Reference | M1 now | Was |
|---|---|---|---|
| 0–100 km/h | ~2.2 s | 2.36 s | 7.6 s |
| 0–160 km/h | ~4.6 s | 4.74 s | n/a |
| Speed after 14 s full throttle | ~230–240 km/h | 230 km/h | ~165 km/h |
| Steady cornering at 60 km/h, full lock | not measurable from video | 0.84 g, ~32 m radius | 0.85 g |
| Braking | not measured | 179→0 km/h in 103 m (≈1.15 g) | 86→0 in 28 m |
| Chase camera (default) | DD2 framing | DD2 framing (31% / 87% / 56% / 49%) | ~3.4 m up, ~10.5° down |
| Far chase camera | DD1 framing | DD1 framing (19% / 69% / 47%) | n/a |
| Bowl floor | grey asphalt | grey asphalt, grass outside | sand |

## How the tuning gets there

- **Power-limited drive:** 440 kW with a 19.5 kN launch cap. Drag is 1.2 N/(m/s)², which gives a top speed of about 255 km/h.
- **Friction ellipse:** drive and brake forces may use 3× the cornering grip. That gives DD's launch without making the car corner harder. Light traction control still keeps drive from eating the grip a corner needs.
- **Gear top speeds:** 16, 28, 40, 54 and 72 m/s. These only drive the RPM for the HUD and, later, the engine sound.
- **Saved tunings:** files are versioned, so a tuning saved with older defaults is ignored rather than silently overriding the new ones.

## M1b: crashes, spins and the derby (calibrated in the grey box)

Hit energy is measured from pre-solve velocities. Two 1400 kg cars give these energies:

| Closing speed | Car-car energy | Into a wall |
|---|---|---|
| 20 km/h (a deliberate ram) | 10.8 kJ | 21.6 kJ |
| 40 km/h | 43 kJ | 86 kJ |

Probe measurements:

| Hit | Damage |
|---|---|
| T-bone at 40 km/h | victim's side −15, attacker's front −18 |
| Head-on into a wall at 40 km/h | front −33 |

**Damage curve** (`RulesConfig`):
- Above 25 points, damage from a single hit grows slowly.
- One hit can never take more than 45 points from a zone, so a front survives at least two of the biggest crashes.
- Repeated contact within 0.8 s deals 30% (grinding in piles).
- Sliding contact above 20 km/h grinds the touching zone (squeezes).
- Hits on a destroyed side or rear pass half their damage on to the engine.

**Spins:** a 12 km/h tap on the rear quarter spins an unprepared car 90–180° (DD Code T2, 10/10). The spin assist and a 0.8 s post-hit grip loss do this calibration.

**DD2 Fast Steer:** at derby speeds it loosens the rear and adds yaw. A last-moment turn in a head-on then takes the hit on the side: your front stays untouched while the other car loses its front (T5, 10/10).

**DD Code tactics** (`DdCodeTest`, 10 trials each):

| Tactic | Result |
|---|---|
| T1 brake-check | 100% |
| T2 rear-corner turn-in | 100% |
| T3 ram into a braking zone | 100% |
| T4 squeeze into a wall or wreckage | 100% (90% in M1b) |
| T5 last-moment turn | 100% |

**Derby pacing** (sim harness, PlayerProxy "average fan", 60 derbies, reproducible since M1c): all plan §17.4 derby targets but one are met.

| Metric | Result | Target |
|---|---|---|
| First contact | 1.7 s (DD1: ~2.5 s to the centre pile-up) | < 3 s |
| Longest idle stretch with ≥ 5 cars (median) | 6.8 s | ≤ 8 s |
| Derby length (median) | 161 s | 120–240 s |
| Player survival (median) | 110 s | 60–150 s |
| Player hits per minute | 18 | ≥ 8 |
| Points from deliberate hits | **55% ❌** (median per derby; 55% over all points) | ≥ 70% |
| Player wrecked within 30 s / 60 s | 0% / 15% | ≤ 5% / ≤ 15% |
| Player wrecked by 3+ cars at once | 2% | ≤ 10% |
| Player stuck | 4.3% | ≤ 5% |
| Derbies that end | 100% | 100% |

- The bot wins 23% of derbies and finishes 8.7th on average out of 20.
- **Deliberate points:** 55% of the bot's points come from aimed hits (throttle on, heading within 45° of the victim, plan §17.4). Another 30% come from hits it made at a wider angle: side-swipes and last-moment turns (T5). The last 14% come from cars that wrecked themselves on it, which DD credits to you. Open for your derby playtest: either the scoring credits too much incidental contact, or the 45° rule misses DD Code moves.
- **Correction:** M1b reported all targets met over 30 derbies. That run was one random draw: the engine stutter of damaged cars used Godot's global random generator, which is seeded differently on every launch. Since M1c the sims are reproducible. Re-measured, the first minute was too harsh, so the opening crunch is now ×0.5 ramping over 75 s (was ×0.6 over 60 s).
- The latest report is in `out/sims/`.

## M1c: Wreckin' Racing on the figure-8 (calibrated in the grey box)

**The layout** (`FigureEightTrack`):
- Two 70 m turns, joined by two straights that cross at grade at 76°: DD1's blind crossover.
- 13 m wide (three cars abreast), 984 m per lap.
- The AI laps in about 37–38 s (the fast drivers at full pace in 37 s); the PlayerProxy alone laps in about 38 s (plan: 35–55 s laps, so lapped traffic keeps meeting you).
- Cornering speed is about 80 km/h in the turns; the straights end at about 150 km/h.

**Wreckin' pacing** (sim harness, PlayerProxy "average fan", 20 races, 6 laps):

| Metric | Result | Target |
|---|---|---|
| Spins in the field per lap (median) | 6.7 | ≥ 3 |
| Crossover incidents per race (median) | 19.5 | ≥ 2 |
| Player position changes per race (median) | 32 | ≥ 6 |
| Leader spun per race (median) | 2.0 | ≥ 2 |
| Cars within 3 s of the player (median share of race time) | 87% | ≥ 60% |
| Racer vs. hunter bot points, equal skill (mean, 10 seeds) | 44 vs 39 (11% apart) | within ±15% |
| Player wrecked before half distance | 0% | ≤ 10% |
| Player stuck | 0.7% | ≤ 5% |
| Races that end | 100% | 100% |

- The bot finishes 60% of races, in 15th place on average; it is wrecked late in the others.
- About 5 of 20 cars are wrecked per race, and 11 take the flag. The winner needs about 250 s for the 6 laps.
- **Crossover incidents (about 20 per race) are far above the minimum.** Whether that is DD1 chaos or too much is for your playtest to decide.
- The latest reports are in `out/sims/`.

**Strategy balance** (plan §17.4): over 10 seeds, a racer bot and a hunter bot of equal skill (0.55) scored **44 vs 39 points** on average, 11% apart (target ±15%). The hunter is wrecked more often (in 15% of its races before half distance); the racer collects the finishing bonus.

**Calibrations ✱** (plan §20.2):
- **A21, race damage ×0.5.** At full damage, the player was wrecked before half distance in 83–100% of races, with 12 of 20 cars wrecked per race.
- **A22, AI pace ×0.97 in Division 4.** At ×1 the AI racers out-pace an average player, and hunting paid 40% more than racing.
- **A23, crossover walls.** The walls open where the legs cross. Where a wall resumes it is flared (about 8°), and the four corners are closed. Blunt wall ends had wrecked cars that drifted into the gap.

**What the PlayerProxy needed to race like a player:**
- **Tap steering.** It taps the keys knowing how the keyboard filter responds (ramp-in 4.5/s, return 8/s), in taps of at least 50 ms. Fixed-rhythm taps gave a third of the steering asked for, so it ran wide in every turn.
- **Anticipation.** It steers from where the car will be in 0.25 s, offsetting its own 150–250 ms reaction time.
- **Smaller mistakes at speed.** A mistake at speed is a twitch, not a held wrong turn: at 150 km/h a 0.3 s full-lock mistake crashed it every lap.
- With these, it laps alone with almost no wall contact (4 of 4 clean runs).

## Still to measure (M2 and later)

- DD2 rollovers and flying parts.
- Pit-stop timing.
