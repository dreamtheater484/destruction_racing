# Progress

## M0 Foundation and references — done (2026‑10‑14)
- **Engine:** Godot 4.7.2 .NET and export templates installed (both SHA‑512 verified against the official release).
- **Repo:** git repo initialised (nothing committed yet); offline NuGet config.
- **Rules library** (`src/Rules`): rulesets Standard (DD2 PC) and DD1 Classic (PC), scoring, spin tracker, hit energy, wreck attribution, damage zones and the wreck rule, championship. **58 unit tests passing.**
- **Dev tooling:** build/test/run/capture/bake scripts. Automated screenshots work.
- **Unattended LightmapGI baking works** (`addons/autobake`), so plan §8.1 needs no fallback.
- **Docs:** `docs/reference_board.md` and `docs/fallback_car_models.md`.
- **Not done yet:** the reference feel sheet (needs your OK to watch DD gameplay videos).

## M4 Vertical slice: Destruction Derby — approved (2026‑10‑15: "And it's good!")
- **Play it yourself:** double-click **Play Derby.cmd**.
  - **Set-up screen:** pick the light (afternoon, golden hour, night), your car (Kitsune, Brougham, Nordvik) and the division (4 = rookies … 1 = the best). ↑/↓ choose, ←/→ change, Enter start.
  - **The derby:** 20 cars, last car running wins. Tab = standings, Esc/P = pause. When you are wrecked: ←/→ watch another car, T = TV or chase camera, Space = ×2 speed, Enter = skip to the results.
  - **Results:** the points tally up per car, then V = the replay, R = race again, M = menu, Q = quit.
  - **Replay:** Space pause, S slow motion (½, ¼), ←/→ −/+10 s, F next car, C camera (TV or chase), Esc back. It starts 8 s before your wreck (or the last 30 s).
- **The Bowl, final art** (plan §7.3, §8): three baked presets.
  - **Afternoon:** the M2 look (HDRI sky, sun, baked lightmap).
  - **Golden hour:** low sun at 11°, a procedural sky with a three-stop gradient, two rings of mountains, the floodlights on.
  - **Night:** a starry sky, floodlights with volumetric cones, lit lamp heads, and real headlights on the six cars nearest the camera.
  - The floodlights flare up when it gets down to 5, 3 and 2 cars. The scoreboard shows the cars left.
  - **Ground marks** (moved here from M3): rolling ruts that build up where the field drives, darker marks from sliding, locked or spinning wheels, gouges from a hub without its wheel, an oil trail from a car with a critical front, and an oil pool under each wreck. By the end the floor shows the whole fight.
- **Crowd** (plan §8.5): about 1,500 spectators on the two grandstands (one MultiMesh, animated in a shader). They cheer with big hits and wrecks, get louder as the field shrinks, and take flash photos at night.
- **Derby AI and director** (plan §10.4):
  - **Behaviours:** opening rush, hunt (rear quarter for spins), squeeze (DD Code T4: pin a car against the wall or a wreck), reverse-ram with a weak front, last-second turn (T5), a wreck as cover, circling, wall avoidance, recovery.
  - **Targeting:** distance, angle, exposed side or rear, finishing a wreck, revenge, the ×2 leader, and the player-attention budget (at most 2/3/4 cars hunt you, by division; 2 in the first minute).
  - **Against exploits:** a player who stops attacking is hunted from further away and by more cars (from 20 s without a real hit). The same goes for a player who rams almost only tail first. A car backing up shows its engine, so hunters go for its front. Its tail coming at you: swing past to its flank, or turn at the last moment.
  - **Director:** wakes passive cars when nothing happens for 4 s; nudges the last cars together.
- **Broadcast HUD** (plan §13; Barlow Condensed): points and speed, position, cars left and time, the damage diagram (critical zones throb red), hit popups, the event feed, banners (FINAL FIVE, LAST THREE, FINAL DUEL "YOU vs #N", YOU WIN!), how many cars are hunting you, the wrecked screen, and the results with an animated tally. Wreckin' Racing uses the same HUD.
- **Juice:** a short slow motion on huge hits (at most once per 10 s), a chromatic flash when you are in a huge hit, camera punch, ROLLOVER popups.
- **Audio v1** (plan §12):
  - **Mix:** buses for music, announcer and world (effects, engines, crowd), ducking under the announcer and after crashes, a muffle during slow motion, a limiter on the master.
  - **Effects:** 45 synthesized sounds (87 files) by `tools/sfxsynth`: crashes by size and surface, glass, scrapes on metal and on the tyre wall, tyres on dirt, squeal, hub grinding, fire, steam, the crowd, stings, the countdown, UI.
  - **Engines:** the M2 granular engines on the six cars nearest the listener; your engine misfires with a critical front.
- **Announcer v1:** 345 lines in 40 categories: intro, start, opening crunch, big hits, spins, wrecks (with the car number: "number 23 is out!"), the leader, cars left, the final duel, your damage (smoke, fire, lost wheel, hood, glass), lulls, the winner, the results. No line repeats within 3 minutes; important calls wait for a gap or cut in.
  - Voiced offline with Kokoro-82M (Apache 2.0, voice `am_fenrir`) through a stadium PA chain. The model is not shipped.
  - **Your own voice:** any line can be replaced by a recording with the same name in an `Announcer` folder next to the game or in `user://announcer` (`docs/announcer_recording_guide.md`).
- **Music:** eight royalty-free tracks, shuffled: jungle and drum & bass (Final Hour, Warriors Fall, Drive) and metal (Harmful or Fatal, Metalmania, Ready Aim Fire, Dragged Through Hellfire, Gretsch Thrash Gretsch), all matched to the same loudness. The music comes up as the field shrinks and fades out for the results and replays. Your own OGG or MP3 files in a `Music` folder in the project join the playlist (git ignores that folder). Sources and licences: `CREDITS.md`.
- **Replays (basic):** every car's pose, wheels and zone health at 30 Hz plus the hits. Crush, smoke and fire replay; dents and parts that came off show as they ended (full damage replay is M8).
- **Checks** (plan §17, all headless and reproducible):
  - **Derby sims**, 80 derbies with the PlayerProxy "average fan": **every plan §17.4 target is met.**

    | Metric | Result | Target |
    |---|---|---|
    | First contact | 1.7 s | < 3 s |
    | Longest idle stretch, ≥ 5 cars (median / p90) | 6.3 / 9.2 s | ≤ 8 s |
    | Player hits per minute | 22.5 | ≥ 8 |
    | Event length | 175 s | 120–240 s |
    | Player survival | 109 s | 60–150 s |
    | Points from deliberate play (aimed hits only) | 100% (71%) | ≥ 70% |
    | Player wrecked within 30 s / 60 s | 0% / 12.5% | ≤ 5% / ≤ 15% |
    | Wrecked by ≥ 3 cars within 3 s | 2% | ≤ 10% |
    | Stuck or recovering | 2.5% of the time | ≤ 5% |
    | Time-outs | none | none |

    The bot wins 18% of the derbies (last car running).
  - **Dominant-strategy bots** (plan §17.4; share of derbies won as the last car running, target ≤ 40%):

    | Bot | Wins |
    |---|---|
    | Hide | 10% |
    | Pure ram | 0% |
    | Spin-farm one victim | 0% |
    | **Reverse only** | **48% ❌** (40 derbies) |

    - **Reverse-only is the one target still missed.** It was 85–100% at the start of the M4 tuning. Most wrecks come from a car's own front: its own rams and head-ons cost the average fan about 50 of the 78 front points it loses in the first minute. A car that only ever hits tail first skips all of that, and the rear is 2.5× as tough as the front.
    - **What brought it down:** hunters go for the engine of a car backing away; against a tail coming at them they swing to the flank or turn at the last moment; the field gives more attention to a player who rams almost only tail first; and a ruined rear now costs rear wheels.
    - **What didn't help:** a softer rear (toughness 2.0) left reverse-only at 60% and pushed the average fan's early wrecks to 32%.
    - This is for your playtest: in DD1, reverse-ramming was the strong tactic too.
  - **DD Code T1–T5:** 10/10 each.
  - **Wreckin' sims**, 40 races (regression for the damage changes): all targets met (7–8 spins per lap, 22–25 crossover incidents, 31–35 position changes, the leader spun twice (median), 86–91% of the race with cars within 3 s, 0% wrecked before half distance, under 0.5% stuck, no time-outs).
  - **86 unit tests** pass (new: rear hits reach the rear axle once the rear is ruined).
  - **Performance** (RTX 5090, 1080p, night, the heaviest preset): 182–237 fps on average, 1% lows of 124–180 fps after the opening (83 fps in the first 10 s while shaders compile), up to 6.5k draw calls in the opening crunch, 0.8 GB VRAM.
- **Rule and calibration changes** (✱, from the sims):
  - **Opening crunch:** damage now starts at ×0.4 and ramps to full over 90 s (was ×0.5 over 75 s). The M4 field attacks harder than the M1b one.
  - **Attacker's share** (new, `AttackerDamageShare` 0.7): in a one-sided ram the attacking car takes 70% of the contact damage. A 40 km/h T-bone now costs the victim's side and the attacker's front about the same, 14–15 each (the attacker used to lose 18 against the victim's 15). Head-ons stay at full damage on both, so the last-second turn (T5) still pays. Ramming still costs engine (plan §6.1).
  - **Heavy rear damage** (plan §6.1, now complete): below 25% rear health, a hit on the tail also reaches the rear axle, so rear wheels can come off (Standard rules).
  - **Stuck cars:** an AI that barely moves now notices it and backs out, and a car steering hard at a standstill keeps its throttle. In one sim the last two cars sat against opposite walls for seven minutes.
- **Metric definitions** (plan §17.4, stated so you can check them):
  - **Deliberate points:** points from aimed attacking hits (throttle on, heading within 45° of the contact), from cars that hit your side just after a last-second turn (T5), and from grinding a car against the wall or a wreck on the throttle (T4). The strict reading (aimed hits only) is reported next to it.
  - **Stuck:** trying to move at under 1 m/s, except while shoving a running car in front of you.
  - **Idle stretch:** no hit of 9 kJ or more anywhere while 5 or more cars run.
- **Acceptance** (plan §18): you played it and approved it on 2026‑10‑15, then approved all eight music tracks and picked the licence.
- **Licence:** the code is under PolyForm Noncommercial 1.0.0 (`LICENSE`), the project's own art, sound and text under CC BY-NC 4.0 (`LICENSE-ASSETS.txt`); third-party files keep theirs (`CREDITS.md`).
- **Still open for later:** the reverse-only bot (48% wins, see the checks above). Revisit if reverse-ramming feels too strong in play.

## M3 Full damage and VFX — approved (2026‑10‑15: "Yes, it looks bloody good!")
- **Look at it yourself:**
  - **Damage Lab.cmd**: a parked car in the Bowl takes real hits from ram cars, so the whole chain runs (impact → referee damage → crush, dents, parts, glass, smoke, fire). 1 = front ram, 2 = rear ram, 3/4 = left/right T-bone, 5 = corner (Shift = harder), 6 = wall scrape, 7 = roll it, 8 = wreck it, 9 = knock a wheel off, R = fresh car, Tab = next model, Space = slow motion. The keys are on screen.
  - **Pileup Test.cmd**: the 20-car stress scene with all effects; shows the frame rate and the PASS/FAIL checks on screen. R = again.
  - **Play Derby.cmd** and **Play Wreckin.cmd**: all 20 cars are now the generated Kitsune, Brougham and Nordvik with full damage (you drive the Kitsune; `--car=brougham|nordvik` picks another). **Play Driving Test.cmd** also uses the models now.
  - Renders: `out/lab/sheet_<car>.png` (made by `tools/dev/lab.ps1 -Model <car>`).
- **Parts that come off** (plan §6.2), driven by the zone health of the rules library:

  | Part | Works loose | Comes off |
  |---|---|---|
  | Front / rear bumper | below 55% / 45%: hangs by one end and swings | below 30% / 20% |
  | Hood | front below 20%: flips up and flaps | front below 8%, or wrecked: blown off |
  | Boot lid | rear below 25% | rear below 10% |
  | Doors (front / rear) | side below 35% / 25%: swing open | side below 12% / 5% |
  | Mirrors | – | side below 40%, or a hard hit next to them |
  | Wheels | – | when the rules say the wheel is lost |

  - Loose panels swing on their hinges, shaken by the car's accelerations and flapping at speed.
  - A part that comes off becomes physics debris that keeps its dents and paint wear (a frozen copy of the dent volume). At most 150 pieces live at once (the oldest go first); a piece at rest turns static, and cars drive over it.
- **Glass and lights:** panes near a hit crack, then shatter into a shard burst (also from heavy crush or a roll). Lamps break: dark, crazed lens.
- **Smoke and fire** (plan §6.1): steam below 60% front, grey smoke below 35%, black below 15%; fire below 8% front and for 9 s after a wreck, with a flickering light (no volumetric fog, plan §8.1). The flames ride with the car and lean back in the draught.
- **Sparks, chips and dust:** impact sparks, a spark stream while metal grinds along a wall or another car, hub sparks from a corner that lost its wheel, paint chips, glass shards, dust where parts land. All are pooled GPU particles with our own shaders (no textures).
- **Engine bay:** a block, rocker covers, air cleaner and radiator under the hood, so a car that lost its hood shows an engine instead of a hole. It dents and crushes with the front.
- **Crushed cars get shorter** (plan §6.2): the nose and tail boxes of the collider shrink with the crush. The collider is four boxes (nose, middle, tail, cabin) sized from each car's model.
- **Rollover:** the roof caves in and some glass bursts.
- **Fixes found on the way:**
  - **Sims are reproducible again.** Jolt reports a body's contacts in the order its worker threads find them; the impact system now sorts them, and a car reports up to 48 contacts (the model collider produces more than the old 12). Two identical runs now match race for race.
  - **Cars riding on cars:** in one derby the player's car climbed onto another car's nose and rode there for five minutes (the wheels only touch the ground, so it had no grip up there), and the derby timed out. A car resting on another car now slides off while its driver tries to drive. And a car that tries to move but stays within 1.5 m for 10 s now counts as immobilised (✱ A9), so a derby always ends (plan §6.0). **85 unit tests.**
  - **Hitches:** dent volumes upload only the slices a hit touched, at most 4 cars per frame (plan §6.2); the effect and debris pools are made while the arena loads instead of at the first crash.
- **Checks** (plan §17.1, all headless and reproducible):
  - **DD Code T1–T5** for every car size: grey box 10/10 on all five; Kitsune, Brougham and Nordvik 9–10/10 (target ≥ 6/10).
  - **Handling scenarios:** the grey box is unchanged from M1 (0–100 km/h 2.36 s, 179→0 km/h in 103 m, 0.84 g). The models match it within 1%, except the turn-around: the short Kitsune swings 279°, the long Brougham 234° (grey box 255°).
  - **Pileup** (plan §16): passes headless (top speed 17.6 m/s, settled to 0.02 m/s, no cars inside each other, 0.90 ms per physics tick against the 3.5 ms budget) and with rendering, effects and debris (176–240 fps).
  - **Derby sims**, 40 derbies with the model cars: every plan §17.4 derby target but one is met: first contact 1.7 s, longest idle 6.6 s, 19.1 player hits per minute, events of 145 s, survival 116 s, player wrecked within 30 / 60 s 0% / 11%, wrecked by ≥ 3 attackers 2%, stuck 3.5% (was 6.2% before the fixes), no time-outs. Still missed: 63% of the player's points come from deliberate hits (target ≥ 70%; M1b 55%). That one is for your derby playtest in M4.
  - **Wreckin' sims**, 20 races with the model cars: all targets met: 7.6 spins per lap, 20.5 crossover incidents per race, 37 position changes, the leader spun twice, 87% of the race with cars within 3 s, 0% wrecked before half distance, 0.6% stuck, no time-outs.
  - 85 unit tests pass.
- **Performance** (plan §16, RTX 5090 at 1080p): a 20-car derby driven by the PlayerProxy runs at 235–238 fps on average (the 240 Hz vsync cap); 1% lows are 136 fps in the opening crunch and 220–240 fps after it; at most 2.7k draw calls and 2.5 M triangles; 600 MB VRAM. Earlier in M3, before the upload budget and the pool warm-up, it was 175–190 fps with 1% lows of 44–107. The first run after a shader changes hitches while Godot compiles it (1% lows of 40–65 fps in the pileup); the release build will precompile shaders (Godot's shader baker, M8).
- **Changed from the plan:**
  - **The scrape mask** is the 3D wear volume from M2 (paint → primer → bare steel → rust, plus soot), not a 1024² UV texture. It works on every car without UVs and already holds the scrapes.
  - **Hubcaps and exhaust** do not come off: the generated wheels have only a small centre cap, and the cars have no exhaust part yet. Both come with the roster in M5.
  - **The oil trail** at critical front damage moves to M4, together with skid marks (both are pooled marks on the ground).
  - **Crash sound** is M4 (audio v1).

## M2 Art and audio spike — approved (2026‑10‑15: "It looks stunning and the sound is also pretty good")
- **Kill criterion passed:** we keep our own car generator, damage system and synthesized engines. The purchased-model fallback is shelved.
- **Look at it yourself:**
  - **Car Studio.cmd**: the three cars on a turntable. Tab = next car, B = derby damage, X = repair, Z = zebra check, 1/2/3 = studio, outdoor or arena light. The keys are on screen.
  - **Bowl Look.cmd**: the Bowl in its baked afternoon light, with a staged pileup. V = next view.
  - **Fog Test.cmd**: a burning car laps the Bowl through dust haze. C = chase or fixed camera.
  - **Engine Test.cmd**: the synthesized engines. Up = throttle, D = auto drive through the gears, 1/2/3 = engine.
  - Still renders: `out/gate/` (made by `tools/dev/gate_m2.ps1`).
- **The decision (plan §8.6):** rate the cars and the damage against Wreckfest.
  - At Wreckfest level or better: we keep our own pipeline.
  - Below: we switch to purchased models that fit the rig spec. The pre-scouted shortlist is in `docs/fallback_car_models.md`; nothing is bought without your approval, and you make the purchase.

### Cars: our own generator (`tools/carsmith`)
- **How a car is built:**
  - Each body is a signed-distance shape: plan view, side profile, roof and cabin, wheel arches, a crease line or a bulging side, and a crowned top.
  - It is meshed with surface nets, with every vertex projected exactly onto the surface and exact normals.
  - **Panels are cut out of the body along exact lines.** Each one gets a 5 mm gap with a rolled flange, and the gap's shadow is baked into the vertex colours. Glass sits recessed in a rubber seal.
  - **Detail parts** are separate meshes: bumpers and rubber strips, grilles, light bezels, lamps with fluted or prismatic lenses, door handles, mirrors, wipers, plates, a full interior, tyres with tread and three rim styles.
  - Blender decimates the meshes (keeping panel edges), re-projects them onto the exact surface and exports glTF with the rig-spec node names (`Body`, `Hood`, `Door_FL`, `Wheel_FR` and so on).
  - Build: `tools/carsmith/build.ps1 <car>` → `assets/cars/<car>/<car>.glb`, about 150–180k vertices per car.
- **Template + morph:**
  - `templates.py` holds the 4-door sedan template. The **Brougham** (#1, US full-size, 5.50 m) and the **Nordvik** (#5, EU brick, 4.80 m) are two morphs of it: proportions, roof line, lights, grille, bumpers, trim and vinyl roof.
  - The **Kitsune** (#12, JP wedge coupe with pop-ups, 4.30 m) is the hard case, designed directly. It becomes the coupe template in M5.
- **Zebra checks:** the paint shader has zebra (reflection stripes) and clay modes, so surface wobble shows up before the paint hides it. Renders: `out/gate/<car>/clean/*_zebra_*.png`.
- **Paint (`car_paint.gdshader`):**
  - Liveries: solid, twin stripes, two-tone or side band, with pinstripes.
  - Metallic flake under a clear coat.
  - **Race numbers in DD style**: a white roundel on both front doors and the roof, in our own block digits (`tools/carsmith/digits.py`, no font licence needed). The numbers dent, scrape and rust with the panel.
  - Damage layers: scuffed clear coat → primer → bare steel → rust, plus soot and road dirt.
  - The other car shaders: `car_glass` (tint, Fresnel, dirt, cracks) and `car_surface` (chrome, rubber, lenses, tyres).

### Damage: three layers, all sampled at the rest pose
1. **Zone crush shapes**, one per DD damage zone (front, rear, left, right; 0–1 each). The end of the car is shoved in, the hood or boot lid buckles along sharp diagonal creases, the wings bulge and the nose drops. A flank caves in at the doors while the roof rail holds.
   - *Changed from the plan:* these are computed in the vertex shader instead of authored as blend shapes.
   - *Why:* they work the same on every car, generated or purchased, with nothing to author per car and no extra memory, and the 4 zone values come straight from the damage model of the rules library.
2. **The dent lattice**: a 32×24×80 volume of displacement plus crumple amount, for individual hits. Normals follow the deformation, so dents catch the light.
3. **Crumple folds**: ridged folds that run across the push direction, like accordion folds in a crushed hood. Coarse folds move the silhouette; fine ones are bump-mapped.
- **Paint wear** comes from a second volume (scrape, rust, soot). Glass cracks only where the frame really folded. The plate text follows the metal, and is dropped when its end is badly crushed.
- **Not in M2 (that is M3):** parts that come off (bumpers, hoods, doors, wheels), shattering glass, sparks, debris. Wreckfest has these, so take that into account when you rate the damage.

### The Bowl: look and lighting (`scenes/bowl/BowlLook.tscn`)
- **Generated by** `src/Tools/BowlLookBuilder.gd`. The scene has:
  - a rutted dirt floor (sand/mud blend with circular ruts);
  - a concrete wall with a tyre barrier;
  - a chain-link fence, two grandstands, floodlights and a **DESTRUCTION DERBY** scoreboard;
  - the Alps HDRI as the backdrop.
- **Afternoon preset:** baked LightmapGI, with a dynamic sun lined up with the sun in the HDRI. The bake runs unattended: `tools/dev/bake.ps1 -Scene res://scenes/bowl/BowlLook.tscn`.
- **The staged pileup** (views `pileup` and `closeup`):
  - a broadside Brougham with both flanks stoved in;
  - the Kitsune nosed into its rear quarter;
  - the Nordvik T-boning its far side;
  - a Kitsune limping away on a crushed tail;
  - a second Brougham coming in for more.

### Fog and smoke ghosting test (plan §8.1, `src/Tools/FogTest.cs`)
- **The setup:**
  - The damaged Kitsune laps at 16 m/s with an engine fire: a flickering light plus a particle smoke plume.
  - Dust haze is a noise FogVolume over the baked Bowl.
  - The camera is a moving chase camera, or a fixed camera for pass-bys.
- **The variants:**
  - Godot defaults: reprojection 0.9, 64³ froxels.
  - Ours: reprojection 0.5, 160×128 froxels, fog energy 0 on the flickering light.
  - No reprojection.
- **Result:**
  - No car-shaped ghost trail in any variant.
  - The visible artefact of the defaults is fire-lit fog that lags and pulses behind the car. Our settings remove it.
  - **Adopted:** reprojection 0.5, froxels 160×128 on High/Ultra, fog energy 0 on short-lived lights. Hero smoke stays particle based.
  - Frames: `out/fog/`.

### Engine sound: synthesized, granular (no recordings)
- **Offline synthesis** (`tools/enginesynth/synth.py`):
  - A source-filter model: firing pulses into the exhaust pipe's acoustic modes and a muffler, plus intake roar and valve tick.
  - The cross-plane V8 gets its uneven bank firing, which gives the burble.
  - The output is cut into one-cycle grains at 12 rpm steps × off and on throttle.
- **The three engines:**
  - Brougham: V8, 650–4,800 rpm.
  - Nordvik: inline-4, 850–6,000 rpm.
  - Kitsune: twin-rotor rotary, 900–8,500 rpm.
- **Runtime** (`src/Audio/GranularEngine.cs`): per engine cycle, it blends the 4 grains around the current rpm and load, resamples them to the cycle length and crossfades them through an AudioStreamGenerator.
  - Rendered in Godot: clean sweeps through the gears, no clicks.
  - Recordings: `out/audio/godot_*.wav`.

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
