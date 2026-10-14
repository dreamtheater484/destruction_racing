# Destruction Racing — Implementation Plan

> Working title. A spiritual successor to *Destruction Derby* (1995) and *Destruction Derby 2* (1996): **the same gameplay, modern visuals**.
> Status: **v2.1**, 2026‑10‑14. v1 was revised after an independent review; every finding and what was done with it is in §23. v2.1 adds your answers (PC versions) and the rules checked against the PC manuals.

---

## 0. Executive summary

We build a PC (Windows) game in **Godot 4.7 (.NET / C#)** with **Jolt physics**, played with the **keyboard only**.

**Production pipelines:**
- **Cars:** a Blender 5.1 *template‑plus‑morph* pipeline.
- **Lighting:** baked lighting presets.
- **Audio:** built from real recordings.

**Gameplay:** it recreates DD1/DD2's modes, scoring, damage logic, tactics and championship structure.

**Presentation:**
- Physically based materials.
- Layered body damage: baked crush shapes plus local dents.
- Smoke, dust and floodlights.
- A 90s TV‑broadcast feel.

**Build order: fun first, then beauty, then breadth.**
1. **Grey‑box gameplay first.** Driving feel, then a grey‑box Destruction Derby, then a grey‑box Wreckin' race. You play each one on keyboard before any final art is made.
2. **Art and audio spike** with a hard quality bar: you rate it at Wreckfest level or better, otherwise we switch to purchased car models.
3. **Vertical slice:** the Destruction Derby in the Bowl, polished to final quality.
4. **Racing modes and championship** on the same proven systems.

---

## 1. The user's key guidance → how this plan satisfies it

| # | Key guidance (from the user) | Where it is addressed | How we verify it |
|---|---|---|---|
| K1 | **Must look beautiful and polished** | §7 Visual direction, §8 Rendering and asset pipeline, §12 Audio, §13 UI | **Art + audio gate at M2 with a kill criterion** (≥ Wreckfest level or switch to purchased models); gates at M4/M5/M8; beauty checklist §17.3 |
| K2 | **Same visual style as DD, but much better graphics (not pixelated)** | §7.1 keep/upgrade table, §7.4 reference board (DD1, DD2, Wreckfest, period photos) | Side‑by‑side comparison with the reference board at each art gate |
| K3 | **Gameplay must be the same as in DD1/DD2** | §3 Fidelity rules, §4 Modes (incl. §4.7 **the DD Code tactics** from the original manual), §5 Scoring, §6 Damage, §20 Sources | Rules unit tests; **tactic scenario tests**; a **reference feel sheet** measured from original gameplay footage; fidelity checklist §17.2; the open fidelity questions are settled with you (§22) |
| K4 | **Each race mode as engaging as the original games** | §4.x "Engagement design" per mode, §10 AI, §11 Feedback | **Grey‑box playtests at M1b/M1c before art**; sims with a human‑like **PlayerProxy** bot; engagement **and frustration** metrics with exact definitions (§17.4) |
| K5 | **Mix of European, Japanese and American cars from that era** | §9 Car roster (12 designed: 4 EU / 4 JP / 4 US; **v1 ships 9 at hero quality, 3 per region**) | Roster review at the M2/M5 art gates |
| K6 | **Best engine: Unity or Godot, whichever delivers the best result** | §2 Engine decision (with the honest trade‑offs) | Decision record |
| K7 | **Keyboard only** | §14 Controls (DD key layout, dedicated Reverse, Sticky Keys safe, echo filtering, ghosting test, presets, fully keyboard‑navigable menus) | Feel gates M1/M1b on keyboard |
| K8 | **First version: bowl + track, Derby + Wreckin' Racing, 12–16 AI, full damage ("more or less")** | §18 Milestones, §19 Scope with an explicit **Must / Should / Later** cut line | Release checklist for v1 |

---

## 2. Engine and toolchain decision

### 2.1 Decision: **Godot 4.7.2 (.NET build, C#) + Jolt Physics**

| Criterion | Godot 4.7 (.NET) | Unity 6 | Weight |
|---|---|---|---|
| Rendering ceiling | Very good. Forward+ renderer with PBR (incl. clearcoat), **LightmapGI** with probes, ReflectionProbes, volumetric fog + FogVolumes, SSR/SSAO/SSIL, glow, AgX tonemapping, TAA/FSR2/SMAA, decals, GPU particles (collision, trails), custom post via `CompositorEffect`, and HDR output + AreaLight3D (new in 4.7) | Excellent (HDRP/URP), plus hardware ray tracing and DLSS | Medium |
| Physics for 20 colliding cars | **Jolt** (default since 4.6, AAA‑proven, stable stacking and pileups) | PhysX (also good) | High |
| **Can I (the developer agent) build, run, test and visually check it autonomously?** | **Yes.** Everything is text (`.tscn`, `.tres`, `.gdshader`, `.cs`) and runs from the CLI: headless for simulations and tests, windowed for automated screenshots and video that I inspect myself. No account or licence needed | Partly. It needs a Unity account sign‑in and licence activation (which only you can do), an editor‑centric workflow, fragile scene YAML, and slow batch mode | **Very high** |
| Iteration speed | Fast: small engine, quick startup, fast C# builds | Slower: domain reloads, asset imports | High |
| Cost and licence | MIT, free forever | Free tier with revenue limits | Low |

**Why this gives the best *result*:** what decides the final quality of both the visuals and the feel is how many **build → run → look → tune** loops can be completed. With Godot I can run hundreds of them autonomously:
- rendering screenshots and video;
- running AI‑only and PlayerProxy derbies headless to measure engagement;
- running unit and scenario tests.

Unity's higher rendering ceiling would not survive a much slower, less verifiable loop that also needs you in the editor.

**Honest trade‑offs we accept:**
- **No hardware ray tracing and no DLSS.** We use baked GI, probes, SSR and TAA/FSR2 instead. For mostly static arenas and tracks, baked lighting looks as good or better.
- **A smaller asset store.** We have to build cars ourselves or buy them (see §8.6).
- **Vehicle physics must be custom.** It would be custom anyway, for the DD arcade feel.
- **Jolt contact impulses are only estimates in multi‑body contacts.** Hit energy is computed by our own code (§5.3).

**Why C# over GDScript:**
- It is several times faster for the hot paths: 20 vehicle simulations at 120 Hz, AI, and deformation math.
- Compile‑time type checking catches bugs before running.
- The pure rules (scoring, spin detection, championship) live in a plain C# library that `dotnet test` checks in milliseconds.

.NET SDK 8 is already installed.

### 2.2 Toolchain (verified on this machine)

| Tool | Status | Use |
|---|---|---|
| Godot 4.7.2 .NET + export templates | **To download** (official godotengine.org). *Needs your OK* | Engine, Windows export |
| .NET SDK 8.0.424 | Installed | C# build and tests |
| Blender 5.1 | Installed | Headless Python: car templates and morphs, crush shapes, props, crowd, smoke flipbooks |
| Python 3.11 + numpy | Installed | Audio tools, data tools, sim‑report analysis |
| ffmpeg 8.1 | Installed | Audio processing, video capture of gameplay clips |
| git | Installed | Version control |
| Hardware | RTX 5090, Ryzen 7 7800X3D, 190 GB RAM | Development machine; §16 also targets mid‑range PCs |

---

## 3. Fidelity rules (how "same gameplay" is enforced)

1. **Rules are DD's rules.** Modes, win conditions, scoring, damage → wreck logic, tactics and championship structure are reproduced as documented (§4–§6, sources in §20). Where the sources are silent or disagree, the value is marked **✱**, is a named tunable constant, and is listed in §20.2. The important ones go to you as questions (§22).
2. **Feel is DD's feel.** Arcade handling, heavy sliding cars, spins from rear‑quarter hits, crunchy contact, short races, 20‑car chaos. It is calibrated against a **reference feel sheet**: values measured from original DD1/DD2 gameplay footage in M0, such as relative top speed, lap times, hit‑to‑spin time, hits to wreck a front, and derby duration.
3. **Every non‑DD addition is listed in the fidelity checklist (§17.2)** and classified:
   - **Presentation** (always on): graphics, VFX, audio, cameras, replays, UI.
   - **Rule‑neutral quality of life** (always on): rebinding, restart, Time Trial ghost and medals, settings.
   - **Gameplay‑affecting assists** (on by default, **all off in the "Purist" preset**): keyboard counter‑steer assist, light traction control, pack‑density catch‑up for back‑markers, impact slow‑mo, optional handbrake binding.
   - **Calibration** (always on, not an addition): `SPIN_ASSIST` and post‑hit grip loss exist only to reproduce DD's "tap the rear, watch it spin" behaviour, which raw physics under‑delivers. They are tuned to the reference feel sheet. The opening‑crunch damage scale (A19) is calibration too.
   - **Deliberate changes that you asked for:** the mixed EU/JP/US car roster with differing stats (K5).
4. **No copied IP.** No "Destruction Derby" name or logo, no original assets or names (tracks, drivers, sponsors), no real car brands, logos or model names. Tracks are original designs that follow the same layout *types*.

---

## 4. Game modes, rules and engagement design

> Every mode has two parts: **Rules** (fidelity) and **Engagement design** (why it is fun and how we make sure of it). ✱ = assumption, see §20.2.

### 4.0 Ruleset: "Standard" (v1) and "DD1 Classic" (v1.1)
**You played the PC versions, so the PC versions are the reference.** The rules below were checked directly in the **DD1 PC manual** and the **DD2 PC manual** (§20).

v1 ships **one ruleset, "Standard"**, which follows **DD2 PC**. Our physics and damage features are DD2's: rollovers, flying parts, pit stops.

**DD1 Classic (PC)** is a pure data preset that follows in v1.1 (it is deferred to keep v1 tuning focused).

| Rule | Standard = DD2 PC (v1 default) | DD1 Classic = DD1 PC (v1.1) |
|---|---|---|
| Grid size (all modes) | 20 cars (you + 19 AI): 4 leagues × 5 drivers (DD2 manual) | **16 cars**: 4 leagues × 4 drivers (DD1 PC manual) |
| Spin 90° / 180° / 360° | 10 / 25 / 50 (DD2 manual) | 2 / 4 / 10 (DD1 manual) |
| Wreck a car | 25 (DD2 manual) | 10 (DD1 manual) |
| ×2 multiplier | All points ×2 if **the car you damage is in 1st place** (DD2 manual) | The same: "doubled if your opponent's car was in the lead when you attacked it" (DD1 manual) |
| Hitting an already‑wrecked car | 0 | 0 ✱ |
| Points cap per event | 999 | 99 (DD1 manual) |
| Wreckin' finishing bonus 1st/2nd/3rd | 25 / 15 / 10 ✱ | 10 / 6 / 4 |
| Stock Car finishing points 1st…20th | 50, 40, 35, 30, 25, 20, 18, 16, 14, 12, 10, 8, 7, 6, 5, 4, 3, 2, 1, 0 ✱ | the same (shortened to 16 places) |
| **The Bowl** | **Spins and wrecks both score.** Both manuals list the table under "Destruction Derby *and* Wreckin' Racing". **The last car still running wins** ✱; survivor bonus 25 ✱ | The same, with DD1 values; survivor bonus 10 ✱ |
| Race end | Continues until **the player** finishes or is wrecked ✱ | Ends when **the first car** crosses the line ✱ |
| Rollovers and jumps | Yes; flipped cars are **auto‑righted after a time penalty** (DD2 manual) | No (strong roll stabilisation) |
| Wheel loss | Yes (heavy rear or wheel damage) | No |
| Hood/trunk fly off, fire | Yes | Cosmetic only |
| Pit stops | Wreckin': **once** per race; Stock Car: as often as you like; **5 s limit per stop** (DD2 manual) | None |
| Championship | 4 leagues × 5 drivers, start in Div 4. Wreckin' season = 5 races, the last one in the Bowl. Stock Car season = 4 races. No promotion → repeat the same tracks. Relegation is not mentioned (DD2 manual) | 4 leagues × 4 drivers. Wreckin' season ends in the Bowl. No promotion → repeat the tracks (DD1 PC manual) |
| Roof colour per division | Div 4 yellow · Div 3 blue · Div 2 red · Div 1 black (DD1 PC manual's scheme; DD2's is not documented) | the same |

### 4.1 Destruction Derby (arena / "the Bowl")
**Rules**
- **The field:** 20 cars in the Bowl. **The last car still running wins.** The finishing order is the reverse elimination order.
- **Scoring:** **spins and wrecks score** (the PC manuals). Event points (spins + wrecks + survivor bonus) feed the championship.
- **Out:** a car is out when wrecked (§6.0). Wrecked cars **stay as obstacles**, smoking or burning, and can still be pushed.
- **No time limit** by default (none is documented). An optional limit exists in Single Event; when it runs out, ranking is by points, then remaining health.
- **When you are wrecked:** "WRECKED – Race Over". Choose **Spectate** (TV cameras, ×2 speed) or **Skip**, which fast‑simulates the rest to the final standings.
- **The ×2 leader:** in the Bowl this is the event points leader ✱, marked with a gold beacon.

**Engagement design** (the Bowl was the "high point" of DD1 for reviewers)
- **The opening crunch:** 20 cars start in a ring around the edge facing the centre, and **everyone meets in the middle within ~3 s**. It is raw and loud, with no slow‑mo. An alternative start uses two facing rows.
- **"Thinking man's destruction":**
  - **Your front is your life.** A destroyed front means the engine is gone. The rear is tough, so **reverse‑ramming** is a real tactic, as it was in DD1.
  - The manual's tactics (§4.7) all work here: the last‑second turn‑in so the other car takes front damage, squeezing cars into walls and wrecks, and side hits on the rear corner.
  - Every engagement is a choice: hit, dodge, or back in.
- **Spin or finish?** A 360° spin (50) is worth more than a wreck (25), so you can spin a car and leave it alive for more points, or finish it off before a vulture steals the wreck. That is a decision the original also posed.
- **Survival vs. greed:** spin and wreck points reward aggression, while the win and the survivor bonus reward staying alive. That is the core tension. Sims check it isn't solved by a single strategy: hiding, pure ramming, or spin‑farming one victim (§17.4).
- **Hunt the leader:** the beacon makes the points leader obvious. AI vultures and grudge‑holders converge on them, so comebacks are possible.
- **The arena evolves:** smoke builds up, debris piles up, wrecks become cover and obstacles, and the floor ruts. Minute 3 plays differently from minute 0.
- **Escalating finale:** at 5, 3 and 2 cars left, music layers come in, the announcer calls it, and the crowd roars. The final duel gets a broadcast banner.
- **Fair but hunted:** an AI player‑attention budget (§10.4) and personalities make it personal without becoming a gang‑kill. An anti‑stall director prevents dead stretches while many cars remain.
- **Fixing DD's weaknesses:**
  - The camera was too close → adjustable chase distance plus a high "arena" view.
  - Too easy → division‑scaled AI.

**Variants** (Should, §19)
- **Total Destruction** (DD1/DD2): every AI hunts you; you survive as long as possible. Score = survival time plus points.
- **Duel** (DD1): 1 vs 1 CPU.

### 4.2 Wreckin' Racing
**Rules**
- **The race:** 20 cars and a fixed number of laps per layout (6–10, data‑driven, short laps).
- **Total score:** spin and wreck points + finishing bonus.
- **Pit stop:** once per race.
- **Hazards:** wrecked cars stay on track as obstacles. Flipped cars are auto‑righted after a penalty.

**Engagement design** (the core DD race mode)
- **Several winning strategies** (praised in DD2), all viable:
  - *Racer:* win the finishing bonus.
  - *Hunter:* farm spins.
  - *Leader‑killer:* sit in 2nd and spin the leader for ×2, repeatedly (the famous DD tactic). The leader beacon makes it readable.
- **The manual's DD Code tactics are the moment‑to‑moment play** (§4.7): brake‑check tailgaters, turn sideways into the rear corner of someone overtaking, ram from behind into braking zones, squeeze into walls and wrecks.
- **Crossover chaos:** the figure‑8 layout has a **blind crossover**, where traffic crossing at speed causes unpredictable pile‑ups and T‑bones (loved in DD1). AI crossover behaviour varies by personality: brake, gamble, or *aim*.
- **Density by design:**
  - DD1 was criticised for narrow tracks, and DD2 (PC) for opponents vanishing "to the horizon".
  - Our layouts are **3 cars wide** with **35–55 s laps**, so lapping traffic constantly creates encounters.
  - A light catch‑up for back‑markers only (a Purist‑off assist) keeps the field together.
- **Division rivals:** in a championship, roof colours show who is in your division, so you know exactly whom to spin.
- **Pit gamble:** one stop with a repair mini‑game (§6.0). You trade time for health.
- **Last‑lap drama:** a banner, the announcer and the crowd. Damaged leaders limp while hunters close in.

### 4.3 Stock Car Racing
**Rules**
- **Scoring:** 20 cars; points for **finishing position only**. Contact is allowed but **scores nothing**.
- **Damage and pits:** damage still applies (a wreck means DNF), and you can pit as often as you like, 5 s per stop (DD2 manual).
- **AI pace:** the AI drives faster here than in Wreckin' (DD1).

**Engagement design**
- **Clean racing in a dirty field:** the DD1 manual itself warns that drivers like "the Bouncer" still bash you in Stock Car. Our bruiser personalities keep bumping (their aggression drops but doesn't vanish), while racers race. You now *survive* contact instead of seeking it.
- **Racecraft:** racing lines, brake‑dab powerslides (DD2 manual tip), **Fast Steer** for tight sections, defending against bruisers, and pit timing.
- **Close finishes:** AI pace is tuned per division, so the player fights for positions all race. No "impossible to catch or overtake" AI (a DD2 criticism).

### 4.4 Time Trial
**Rules**
- Solo, with a **stopwatch at the bottom of the screen** (DD1).
- Best laps are kept per layout, direction and car.

**Engagement design** (rule‑neutral quality of life)
- Instant restart (≤ 1 s).
- A **ghost of your best lap**.
- Sector splits.
- Bronze/Silver/Gold targets that unlock liveries.
- A local leaderboard per car.

### 4.5 Championship
**Rules (Standard)**
- **Field:** 20 drivers in **4 divisions of 5**. **Every race has all 20 cars**, but you are ranked *within your division*. Each car's roof colour shows its division.
- **Wreckin' championship:** 4 track events + **the Bowl as the finale**.
- **Stock Car championship:** 4 track events.
- **Promotion and relegation:**
  - Finish **top of your division** to go up ✱; otherwise you repeat the same tracks (both PC manuals). The player is not relegated, since neither PC manual mentions it.
  - **To keep divisions at 5 drivers:** when the player goes up, the last AI of the division above drops down. AI drivers also swap between divisions each season (top AI goes up, bottom AI comes down). So rivals recur and change, and the rules are explicit.
- **Unlocks:** each promotion unlocks new layout and variant combinations (reverse, time of day, layout), as DD1 did. Winning Division 1 unlocks a bonus variant and a livery set.

**Engagement design**
- **The Bowl finale can swing 40–50 points (DD1):** the season stays alive until the last event, and sims check how often it is already decided before the finale (§17.4).
- **Personalities and taunts (DD1):** 19 named drivers with grudges, and **text radio taunts** after you hit them (voiced taunts come in v1.1).
- **Readable stakes:** before each event, a division table showing the points needed. Afterwards, an animated points breakdown and a promotion (or repeat) sequence.

### 4.6 Practice and Multi‑Player
- **Race Practice:** any layout, direction, time of day, car and AI count. A free damage test in the Bowl.
- **Multi‑Player (hot‑seat, as in DD1/DD2):** players take turns against the CPU with a shared league table. Moved to **v1.1** (§19).

### 4.7 The "Destruction Derby Code" (the original DD1 manual's five tactics) — design requirements
The DD1 PS manual teaches five tactics. Each one **must reliably work** for the player, **must be used by the AI** (as personality allows), and **must work against the AI**. Each has an automated **scenario test** (two scripted cars with a fixed setup, success rate ≥ 60% at default tuning) plus a playtest check.

| # | Tactic (paraphrased from the manual) | What it needs from our systems |
|---|---|---|
| T1 | **Brake‑check** a tailgater: they take front damage | Hard braking that is strong and stable; front zone damage on rear‑end hits; AI tailgaters that sometimes get caught |
| T2 | When being overtaken, **turn sideways into the rear corner** of the overtaker: it spins | `SPIN_ASSIST` on rear‑quarter contact; post‑hit grip loss; AI defends with it |
| T3 | **Ram the back of a car braking for a bend**: it nosedives into the wall | Braking zones where the AI brakes predictably; enough push to break traction |
| T4 | **Squeeze cars sideways into walls and stationary wreckage** | Stable sustained contact (no jitter); side damage from walls; wrecks as solid obstacles |
| T5 | In a head‑on, **turn at the last moment**: you take side damage, they take front damage | Correct zone assignment by contact point and normal; the side toughness multiplier; quick steering response (Fast Steer) |

---

## 5. Scoring system

### 5.1 Rules
- **Point values** per ruleset in `RulesConfig`: spin tiers, wreck, finishing tables, survivor bonus and cap.
- **×2 multiplier:** applies when the **victim** is in 1st place at the moment of the hit: race position 1, or the points leader in the Bowl ✱. It covers spin and wreck points.
- **Event total:** `min(cap, Σ spin + Σ wreck + finishing/survivor bonus)`, added to the division table. Tie‑break: more wrecks, then better best finish ✱.

### 5.2 What the player sees
- **World‑space popups plus a HUD stack:** "SPIN 180 +25", "LEADER ×2 → +50", "WRECKED +25".
- **Points total:** a roll‑up animation.
- **Tally screen:** points broken down by category.

### 5.3 Hit energy, spin detection and attribution (implementation)
- **Hit energy is our own calculation, not Jolt's impulse estimate.** Jolt's impulses are unreliable in multi‑body pileups. Each physics step caches every car's linear and angular velocity *before* the solve. For each new car‑car contact:
  - relative contact velocity: `v_rel = (v_a + ω_a × r_a) − (v_b + ω_b × r_b)`;
  - its normal component `v_n`;
  - reduced mass `μ = m_a·m_b/(m_a+m_b)`;
  - **hit energy** `E = ½·μ·v_n²`.
- **What hit energy drives:** hit thresholds, damage, spin‑steal thresholds, camera shake, audio tier, crowd excitement and replay "interest".
- **Hit record:** a contact with `E ≥ E_HIT` creates `Hit {attacker, victim, E, contactPoint, normal, victimZone, time}`. The **attacker** is the car with the larger velocity component toward the other along the normal. A near‑equal head‑on collision makes both cars attacker and victim.
- **Spin window:** a hit opens or refreshes a window on the victim (`SPIN_ATTRIBUTION_WINDOW`, 1.0 s). While it is open, we integrate the victim's yaw (the world‑up component of angular velocity). The window stays alive while the yaw rate is above 60°/s, and closes 0.35 s after rotation stops or after 5 s.
- **Award tiers:** 90°, 180°, 360°. The popup **upgrades live** ("SPIN 90 → 180 → 360"), and the highest tier is awarded when the window closes.
- **Attribution:**
  - Spin points go to the last attacker within the window. A new hit during a spin steals attribution only above `SPIN_STEAL_E`.
  - Wall spins and self‑spins score nothing.
  - Wreck credit goes to whoever dealt the most damage in the last 5 s (none for walls).
- **Anti‑farming:** hits on wrecked or stationary cars score 0, and spins count only if the victim was driving (above `SPIN_MIN_SPEED`) when hit.
- **Tests:** unit tests replay recorded traces covering tiers, attribution, steals, wall spins, spins during rollovers, and pileups with 3+ simultaneous contacts.

---

## 6. Damage model

### 6.0 Rules (from DD1/DD2, §20)
- **Zones:** the DD damage diagram (HUD, lower right): Front, Rear, Left, Right plus 4 wheels, each going green → yellow → orange → red → black.
- **What determines damage:** impact speed and angle.
- **Front = engine/radiator.** Front destroyed → radiator blown → the car stops → **wrecked** ("Race Over" for the player). *This is the primary wreck rule in both games.*
- **Sides:** the car **pulls** toward the damaged side. Both sides damaged → it **barely turns**.
- **Rear:** very tough (DD1 players reversed into opponents). Heavy rear damage reduces acceleration and top speed, and under Standard rules causes **wheel loss**. Lost wheels are never repaired.
- **Near destruction (DD2):** the hood flies off and the engine catches fire.
- **Flip (Standard):** a car on its roof or side for 2 s is auto‑righted after a time penalty (3 s total). On track it is placed on the nearest track point; in the Bowl, in place.
- **Immobilised:** a car that cannot move for 10 s (for example, 2+ wheels lost) counts as wrecked ✱.
- **Pit stops (DD2):**
  - Entering the pit lane auto‑guides you into the garage.
  - The camera orbits the car; select a zone with ←/→ and **mash Enter or Space** to repair it toward green.
  - **5 s limit**, then you are auto‑ejected, and control returns after about 2 s.
  - Wreckin': once per race. Stock Car: as often as you like (DD2 manual). The pit lane is per layout (data‑driven).

### 6.1 Mechanical implementation
- **Damage per hit:** `dmg = k × max(0, E − E_threshold)^0.8 / (zoneToughness × carToughness)`.
  - The zone comes from the contact point and normal in car‑local space, with spill‑over on corner hits.
  - Both cars take damage, so **ramming with your front costs engine**.
- **Zone toughness:** Front 1.0, Sides 1.2, **Rear 2.5**. That makes reverse‑ramming strong but not invincible. It is tuned against the reference feel sheet's "hits to wreck" values.
- **Effects:**
  - **Front:** power × f(health). Below 60%: steam. Below 35%: grey smoke and stutter. Below 15%: black smoke, misfires, hood loose. Below 8%: **fire**. At 0: **wrecked**.
  - **Sides:** toe pull proportional to damage; both sides damaged → maximum lock scaled down to about 35%.
  - **Rear:** up to −30% acceleration and top speed. Below 25%, rear wheels can detach on further hits.
  - **Wheels:** wobble and camber; at 0 the wheel detaches and the corner drops onto a hub skid point (sparks, drag, strong pull).
- **Readability (DD2 was criticised for twitchy steering after crashes):** pull ramps in smoothly and is never jerky. The HUD diagram and smoke colour always explain how the car behaves.
- **Repair:** each pit mash press restores `PIT_REPAIR_PER_PRESS`. Presses are rate‑limited (key echo ignored, max 15 per second).

### 6.2 Visual implementation: a layered, hybrid deformation
*A single coarse lattice gives broad "rubber" dents (cells of about 30 cm), so damage is built from three layers.*

1. **Baked crush shapes (the big, convincing crumples).**
   - **What:** per body template, Blender (headless) generates **crush blend shapes for each zone** (front, rear, left, right, roof) **× 2 severities**. It does this by pressing colliders into the shell with a soft‑body/cloth sim, or with a panel‑aware procedural crumple.
   - **Result:** creases follow panel lines, the hood buckles and the bumpers fold.
   - **Runtime:** shape weights are driven by zone damage and impact direction.
   - **Shared:** because morph variants share their template's topology (§8.6), these shapes are made **once per template** and work on every car built from it.
2. **A fine local‑dent lattice.**
   - **What:** 24 × 12 × 48 points in a 3D RGB16F texture (about 83 KB per car), updated on the CPU around each impact (amortised, at most 4 updates per frame).
   - **Effect:** the vertex shader adds this local, impact‑specific denting on top of the crush shapes.
3. **Surface detail.**
   - **Crumple detail:** normal perturbation weighted by local damage.
   - **Scrape mask:** **1024²**, UV space; it reveals paint → primer → bare metal → rust/soot.
   - **Dirt and soot:** dirt accumulation, plus fire char.

**Other details**
- **Hinged parts deform correctly:** every vertex stores its **rest‑pose car‑local position** in a custom vertex attribute, and the lattice is sampled by that position. An opened door or flapping hood carries its own dents instead of sliding through the field.
- **Detachable parts:** bumpers hang by one side and then fall; the hood flips open, flaps and tears off; doors swing; plus the trunk lid, mirrors, exhaust, hubcaps and wheels. Detached parts **keep their dents**: the current deformation is baked into a mesh copy when they detach.
- **Glass and lights:** glass goes intact → cracked → shattered (shard burst). Lights break, go out and spark.
- **VFX by state:** steam → grey smoke → black smoke → fire with flickering light. Also scrape sparks and an oil trail at critical front damage.
- **Physics shape:** the nose and tail boxes of the compound collider shrink with heavy damage, so crushed cars really get shorter.

---

## 7. Visual direction

### 7.1 What DD looked like → what we keep → what we upgrade

| DD (1995/96) | Keep (identity) | Upgrade (modern) |
|---|---|---|
| Boxy cars with big racing numbers, bright primary colours, sponsor‑style decals, coloured roofs per division | **Bold liveries, big door numbers, division roof colours** | PBR paint with clearcoat and flake; a layered livery system; decals that scrape off and reveal primer and metal |
| The Bowl: a flat arena with walls and tyre barriers, **mountains behind, in daylight** | **The Bowl by the mountains**, tyre walls, concrete, fences, grandstands | Lit dust and smoke, loose physical tyre stacks, a reacting crowd, golden‑hour light |
| Small tracks with crossovers, ovals and scenic backdrops; day/dusk/night variants | **Figure‑8 crossovers, ovals**, time‑of‑day variants | Full 3D environments, dressed infields (trailers, campers, flags) |
| Crumpling shapes, smoke, flying wheels and hoods (DD2) | **Damage as the main spectacle** | Layered crush and dent deformation, detachable parts, glass, fire, steam, sparks |
| Low resolution, flat lighting, pixel textures | — (dropped) | 4K‑ready textures, baked GI with probes, filmic tonemapping, volumetrics |

### 7.2 Art direction pillars
1. **"County‑fair motorsport, filmed for TV."** Gritty, grounded and a little glamorous, with a 90s broadcast vibe in the HUD, the replays and the announcer.
2. **Saturated cars, muted world.** Liveries pop against dusty concrete, asphalt and dirt.
3. **Daylight first, like DD.** The **hero presets are sunny afternoon and golden hour**, the closest to DD's look and the most flattering for paint and metal. **Night under floodlights** and **dusk** are variants (DD1 had day/dusk/night versions).
4. **Everything gets wrecked.** By the end, the venue is full of debris, skid marks, ruts and smoke haze.
5. **Readable chaos.** Your own car (rim light, livery contrast), your damage state (HUD and smoke colour) and threats (audio cues) are always readable.

### 7.3 Environments
- **The Bowl (v1):** a large oval arena with a flat dirt/sand floor and gently sloped perimeter walls (cars ride up and slide back). It has a tyre‑barrier rim, grandstands, floodlight towers, a live scoreboard tower and **a mountain backdrop** (a nod to DD1). Presets: Afternoon (hero), Golden hour, Night.
- **Crossroads Speedway (v1): one venue, three layouts**, mirroring DD1's own approach of reusing tracks as variants.
  1. **Oval** (≈ 35 s lap): banked ends, flat out, pack racing. A nod to DD1's Speedway.
  2. **Figure‑8** (≈ 45 s lap) with a **blind crossover**. A nod to DD1's Crossover and DD2's Motorplex.
  3. **Infield road course** (≈ 55 s lap): uses the infield roads, a **crossroads**, a hairpin and a short jump (DD2 flavour). *Should, §19.*

  All layouts are 3 cars wide, run forward and reverse, have Afternoon, Golden hour and Night variants, and have a pit lane (except the infield course).
  **Season variety:** 3 layouts × 2 directions × 3 times of day gives a proper 4‑race season without repeating a configuration. Each time of day has its own lighting bake and colour grade, so variants look clearly different.
- **v1.1+:** Quarry Run (a chalk quarry with a jump, tunnel and crossroads), DD2‑style arenas (central hump, central pit, edge drop into a crusher), a night city street circuit, a seaside track with hairpins.

### 7.4 Reference board (M0)
A `docs/reference_board.md` with links and screenshots (saved locally only with your OK):
- DD1/DD2 gameplay (the Bowl, crossover pile‑ups, HUD);
- **Wreckfest** (the modern benchmark for car and damage quality);
- period photos of real demolition derbies and 80s grassroots stock‑car racing;
- reference for each car archetype.

Every art gate compares our renders against it.

---

## 8. Rendering, technical art and asset pipeline (Godot 4.7 Forward+)

### 8.1 Lighting and global illumination
- **Static environment:** **LightmapGI baked per time‑of‑day preset**, plus **LightmapProbes**, so cars and debris receive matching indirect light. ReflectionProbes are baked per preset.
  - *SDFGI is not used:* moving objects don't contribute to it, and baked lighting is cheaper and looks better in static venues.
- **Bake automation (an M0 check):** confirm that LightmapGI baking can run unattended (an editor script or plugin that bakes all presets and quits).
  - *Fallback A:* you click "Bake" once per preset (about 1 minute each, only when an environment changes).
  - *Fallback B:* VoxelGI or SDFGI for the affected preset.
- **Day:** a sun with 4 shadow cascades, an HDRI sky (CC0) blended with a procedural sky, and SSAO/SSIL for contact shadows.
- **Night:** 4–6 shadowed floodlight spots, many unshadowed fills, and emissive lamp heads with glow. Car headlights: real spots on the player and the nearest 5 cars; the rest use emissive lenses and light decals.
- **Volumetric fog and smoke (ghosting is managed):**
  - Godot's temporal fog reprojection smears with moving lights and volumes. So: tuned reprojection amount, higher froxel resolution on High/Ultra, and **fog energy 0 on short‑lived lights** (fire flicker, flashbulbs, sparks).
  - **Headlight beams through smoke** use mesh light cones (a soft additive depth‑fade shader) instead of relying on fog.
  - The hero smoke is particle based, and FogVolumes are used only for slow, large haze build‑up.
  - **Test:** a moving chase camera at M2.

### 8.2 Materials (custom shaders)
- **`car_paint.gdshader`:**
  - livery albedo, procedural metallic flake and clearcoat;
  - damage layers (paint/primer/metal/rust/soot) from the scrape mask;
  - dirt;
  - crush shapes (blend shapes) plus lattice displacement (sampled by rest position);
  - crumple normals.
- **Other materials:** tyres (fictional sidewall lettering), chrome, glass with crack blend, headlight lenses (reflector look), concrete and asphalt with rubbered line and skid marks, dirt with ruts.
- **Texture sources:** CC0 libraries (Poly Haven, ambientCG), *with your OK*, plus procedural detail.

### 8.3 VFX
- **GPU particles:** sparks (trails and collision), tyre smoke, lit dust plumes, engine smoke and fire (Blender‑rendered flipbooks), steam, glass shards, paint chips.
- **Skid marks and dirt ruts:** pooled dynamic strips.
- **Physics debris:** a pool of about 150 pieces (bumpers, hoods, wheels, hubcaps); the oldest go to sleep, then become static.

### 8.4 Post‑processing
- **Tonemapping and colour:** AgX, a colour‑grading LUT per preset, glow, auto exposure.
- **Anti‑aliasing and upscaling:** TAA or FSR2.
- **`CompositorEffect`s:** velocity motion blur, a hit chromatic and radial‑blur pulse, subtle grain and vignette.
- **Depth of field:** in the menus and replays only.

### 8.5 Crowds and life
- **Crowd:** a MultiMesh crowd (Blender‑generated variants, vertex‑shader animation) that reacts to big hits; flashbulbs at night.
- **Other life:** waving flags, marshals, and a live scoreboard.

### 8.6 Asset pipeline (how "beautiful" is produced without an art team)

**Cars: template plus morph, not lofting.** *Car surfacing is the hardest hard‑surface problem, and lofted or boolean‑cut bodies show wobbly reflections in clearcoat.*

1. **Templates:** four clean, **subdivision‑ready base bodies**, modelled carefully in Blender Python as control‑cage subdivision models:
   - quads with supporting edge loops;
   - panel gaps as real geometry;
   - wheel arches built into the topology.
   The four templates are a **4‑door sedan**, a **2‑door coupe**, a **3‑door hatch** and an **estate/wagon**.
2. **Morph variants:** each car is a template plus a **lattice and shape‑key morph** (proportions, roof line, hood length, wedge), plus **swappable detail kits**: front and rear fascias, headlights (round, square, pop‑up), grilles, bumpers (chrome blade, rubber, body‑colour), trim, louvres, spoilers, vinyl roof, wood panel.
   - *Because variants keep the topology:* UVs, the rig, crush shapes and LODs are all shared per template.
3. **Quality control in the look‑dev loop:**
   - a Godot "photo studio" renders turntables and 3/4 views under three presets;
   - **zebra‑stripe and matcap reflection renders** catch any surface wobble before it reaches the paint shader;
   - I review every render and iterate.
4. **Output:** glTF with named nodes (rig spec below), LOD0/1/2 and collision proxies, written to `assets/cars/<id>/`.
5. **Art spike (M2) proves the hard cases first:**
   - **Kitsune Rotary GS:** a wedge coupe with pop‑up lights, the hardest shape.
   - **Aldridge Brougham 400:** a US full‑size sedan.
   - **Nordvik Fjord DL:** a European "brick".
   Together they cover 2 templates and one morph, all fully damaged, painted and lit.
6. **Kill criterion and fallback:**
   - At M2 you rate the cars and the damage against the Wreckfest references. If they are not at that level, we **switch to purchased models** that conform to our rig spec.
   - In M0 I compile a **pre‑scouted shortlist** of suitable era‑appropriate models (licence, price, polygon count, panel separation). Nothing is bought without your approval.

**Rig spec** (so generated and purchased models are interchangeable):
```
Car (root) ─ Body ─ Hood · Trunk · Door_FL/FR(/RL/RR) · Bumper_F/R · Fender_* · Mirror_L/R
           ─ Glass_Windshield/Rear/Side_*  ─ Light_Head_L/R · Light_Tail_L/R
           ─ Wheel_FL/FR/RL/RR (Tyre, Rim, Hubcap) ─ Interior (roll cage, dash, wheel)
           ─ Anchors (Exhaust, Smoke_Engine, Fire, Camera_Bumper, Camera_InCar)
vertex attribute CUSTOM0 = rest-pose car-local position; blend shapes Crush_{Front,Rear,Left,Right,Roof}_{1,2}
```

**Tracks:** a spline‑based `TrackBuilder` (C#) turns a `TrackDefinition` into:
- road with banking and surface blending;
- kerbs, walls, tyre walls, Armco and fences;
- pit lane;
- checkpoints, AI lanes, speed profiles and crossover zones.

One venue definition holds several layouts.

**Props and dressing:** Blender generators plus CC0 models — grandstands, floodlights, billboards (fictional sponsors), trailers, campers, tyre stacks, cones, flags, marshal posts, a scoreboard and the mountain backdrop.

**Textures and decals:** CC0 scans plus procedural detail. Sponsors, numbers and logos are our own vector art.

**Effects assets:** smoke, fire and dust flipbooks from headless Blender sims.

**Licence hygiene:** `CREDITS.md` logs every external file with source and licence. Only CC0, OFL, MIT/BSD or licences that you approve.

---

## 9. Car roster (K5)

### 9.0 Relation to the originals
DD1 used one car model in many liveries, and DD2 a stock‑car‑style model. Both offered **Rookie / Amateur / Pro**. The mixed EU/JP/US roster is **the deliberate change that you asked for**. To stay faithful, **Rookie / Amateur / Pro stays as a driver class** on top of any car:
- **Rookie:** high assists; the car auto‑rights after a 180° spin (DD1); slightly less power.
- **Amateur:** the default.
- **Pro:** +8% power, no assists, the most slide.

### 9.1 Roster: 12 designed, **9 in v1 (3 per region)**, cut line 6 (2 per region)
All names are **fictional working titles**, to be checked against real model names before release. The silhouettes evoke era archetypes and are **not replicas**.

| # | Working name | Region | Archetype it evokes | Template | Drive | Spd | Acc | Hdl | Wgt | Tgh | Release |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Aldridge Brougham 400 | US | Late‑70s full‑size sedan | Sedan | RWD V8 | 6 | 5 | 3 | 9 | 9 | v1 (cut‑line 6) |
| 2 | Aldridge Homestead | US | Late‑70s full‑size **wood‑panel wagon** | Wagon | RWD V8 | 5 | 4 | 3 | 10 | 10 | v1 |
| 3 | Columbia Interceptor GT | US | Mid‑80s pony car | Coupe | RWD V8 | 8 | 8 | 5 | 6 | 6 | v1 (cut‑line 6) |
| 4 | Columbia Statesman | US | Late‑70s personal‑luxury coupe | Coupe | RWD V8 | 7 | 6 | 4 | 8 | 8 | v1.1 |
| 5 | Nordvik Fjord DL | EU (SE) | Early‑80s boxy "brick" sedan | Sedan | RWD I4 | 5 | 5 | 5 | 7 | 9 | v1 (cut‑line 6) |
| 6 | Adler Falke 2.7i | EU (DE) | Late‑80s compact sports sedan | Coupe | RWD I6 | 8 | 7 | 8 | 5 | 6 | v1 |
| 7 | Volant 105 GTi | EU (FR) | Mid‑80s hot hatch | Hatch | FWD I4 | 7 | 8 | 8 | 3 | 4 | v1 (cut‑line 6) |
| 8 | Ashford Kestrel RS | EU (UK) | Mid‑70s RWD rally saloon | Sedan | RWD I4 | 7 | 7 | 7 | 4 | 5 | v1.1 |
| 9 | Hikari Kaze GT | JP | Mid‑80s light RWD coupe | Hatch | RWD I4 | 7 | 7 | 9 | 3 | 4 | v1 (cut‑line 6) |
| 10 | Tsubasa Hayate 260 | JP | 70s long‑hood sports coupe | Coupe | RWD I6 | 8 | 7 | 7 | 4 | 5 | v1.1 |
| 11 | Ryujin Raiden GTS | JP | Mid‑80s boxy turbo sedan | Sedan | RWD I6T | 8 | 8 | 6 | 6 | 6 | v1 |
| 12 | Kitsune Rotary GS | JP | Early‑80s rotary coupe | Coupe | RWD Rotary | 9 | 7 | 8 | 3 | 3 | v1 (cut‑line 6) |

*(The ratings are indicative; final values come from simulation tuning.)* With 9 models and 20 cars on the grid, cars repeat. Signature liveries and division roof colours keep every driver recognisable; DD1 itself had one model for all.

### 9.2 Visual identity per region
- **US:** long hoods, chrome, square headlights, vinyl roofs, wood panelling, V8 burble.
- **EU:** clean boxy lines, rubber bumpers, fog and rally lamps, I4/I6 rasp.
- **JP:** pop‑up lights, louvres, wedges, 80s decal graphics, revvy I4 and rotary scream.

### 9.3 Balance
- **Rating budget:** about 32 ± 2 points per car.
- **Archetypes:** *heavy/tough* (US) is strong in the Bowl but slower on track; *light/nimble* (JP/EU hatch) is fast and precise but fragile.
- **Physics compression:** mass ranges 1.0–1.5× (not the real 2×); toughness offsets the rest.
- **Target (no longer contradictory):** each car is **within ±15% of the mean in its intended mode(s)**, and **no car is top‑3 in every mode**. This is checked over 200 sim events per mode. The car‑select "Derby / Race" hint shows each car's strength honestly.

### 9.4 Livery system
- **Layers:** base → second colour (two‑tone, stripes, panels) → **big number** (doors, roof, hood) → fictional sponsors → pre‑existing wear (rust, primer panels).
- **Roof colour:** set by division in the championship.
- **Composition:** a `SubViewport` composes the layers into one 2048² albedo per car at load.
- **Colour choice:** the player picks from a **keyboard‑friendly palette grid** (no mouse colour wheel). AI drivers have fixed signature liveries.

---

## 10. AI

### 10.1 Shared foundations
- **Racing line:** a precomputed centre and racing line per layout, with 3–5 lanes and a curvature speed profile.
- **Sensors:** raycasts plus a list of nearby cars (relative position and velocity, damage, intent).
- **Same physics as the player:** the AI uses the same physics and `ICarInput` as the player (no cheating). Recovery from spins and getting stuck is built in. Flips follow the §6.0 rule.

### 10.2 Driver personalities: 19 named drivers (as DD1's 19), recurring across the championship
- **Traits** (0–1): `skill`, `aggression`, `vengefulness`, `opportunism`, `self‑preservation`, `consistency`.
- **Archetypes:**
  - *Bruiser:* bashes even in Stock Car, like the manual's "Bouncer".
  - *Racer.*
  - *Vulture:* steals wrecks.
  - *Grudge‑holder.*
  - *Psycho:* provokes collisions, as in DD1.
  - *Rookie:* inconsistent.
- **Identity:** name, nationality, car, number and signature livery.
- **Taunts:** text radio banners with name and portrait after hits, with a per‑driver cooldown and a frequency setting. Voiced taunts come in v1.1.

### 10.3 Racing AI (Wreckin' / Stock Car)
- **Driving:** pure‑pursuit on the chosen lane, a curvature speed controller scaled by skill, lane‑switch overtaking, and crossover behaviour by personality.
- **DD Code (§4.7) on offence:** aggressive drivers evaluate T2 (rear‑corner turn‑in on cars overtaking them), T3 (ram into braking zones) and T4 (squeeze into walls or wrecks). Expected value = points − risk to their own front.
- **DD Code on defence:** AI can also *fall for* the tactics. Tailgating AI with low skill gets caught by a T1 brake‑check, and naive AI brakes predictably into bends (T3 bait).
- **Stock Car:** less aggression but **not zero**. Bruisers and the Psycho still bump, and racers race cleanly but defend.
- **Pit decision:** based on damage and remaining laps.

### 10.4 Derby AI (arena)
- **Utility‑based target selection:** distance and closing angle, exposed weak zones, **finishing a wreck**, revenge, the ×2 leader, and the **player‑attention budget**.
- **Behaviours:** `Hunt`, `Ram`, `ReverseRam` (high self‑preservation, more often as front health drops), `LastSecondTurn` (T5), `Squeeze` (T4), `Evade`, `Circle`, `UseCover` (a wreck as a shield), `Recover`.
- **Player‑attention budget:** at most N AI target the player at once (N = 2/3/4 by division). It is measured as a frustration metric (§17.4). A player who camps or hides gets more attention, so hiding doesn't work.
- **Anti‑stall director:** while 5+ cars remain, if no hit happens anywhere for 6 s, the most passive AI become more aggressive. For the final duel, it nudges both cars together without forcing them.

### 10.5 Difficulty
- **Scaling:** division 4 → 1 scales skill, aggression and reaction time, and a global Easy / Normal / Hard (DD1 had three) shifts the whole curve.
- **The window to stay in:** DD1 was "too easy" and DD2 "unfair, impossible to catch". The target is in between, verified by PlayerProxy sims and frustration metrics at Normal.
- **Stock Car pace:** the AI is faster in Stock Car than in Wreckin' (DD1).
- **No hidden rubber‑banding on leaders.** Only the Purist‑off back‑marker catch‑up.

---

## 11. Feedback and "juice"

| Event | Visual | Camera | Audio | UI |
|---|---|---|---|---|
| Light contact | Sparks, paint chips | Micro shake | Tap, scrape | — |
| Big hit (by hit energy) | Spark burst, crush, debris, glass | Shake by E, FOV punch, a 1‑frame chromatic flash | Layered crunch (thump + metal + glass + debris), crowd "OOOH" | Damage diagram flashes the zone |
| Spin scored | Victim tyre smoke | — | Rising sting per tier, announcer | Popup upgrades live, points roll |
| Wreck (by player) | Fire or smoke burst, hood blows | Slow‑mo 0.25 s at 35% (at most once per 10 s; Purist: off) | Big crunch, crowd roar, announcer | "WRECKED +25", wreck feed |
| Rollover | Roof crush, glass burst | Stays upright, smooth follow | Rolling impacts | "ROLLOVER!" |
| Own critical damage | Own smoke and steam | — | Misfires, warning beeps | Zone pulses red |
| Final 5/3/2 cars | Floodlight flare (night) | — | Music layer, announcer | Banner |
| Last lap | Flag | — | Announcer, crowd swell | "FINAL LAP" |

---

## 12. Audio

- **Engines: granular synthesis from real recordings.**
  - **Sources:** real on‑board and exhaust rev ramps per engine type (cross‑plane V8, I4, I6, I6 turbo, rotary), from CC0 or royalty‑free libraries, *with your OK*. Some libraries need a login; you would download those files yourself.
  - **Runtime:** a granular engine with RPM and load crossfades, plus damage layers (misfire, rattle).
  - **Detail:** the nearest 6 cars get full detail; distant cars a cheaper pack drone.
  - **Fallback:** a pulse‑train synth if no suitable recordings are found.
- **Impacts:** recordings layered by hit‑energy tier and material (metal/metal, concrete, tyre wall, glass), *with your OK*.
- **Continuous sounds:** scrape loops, tyre squeal per surface, dirt spray, wind, transmission whine, hub grinding.
- **Crowd:** an ambience bed plus reactions driven by an "excitement" value.
- **Announcer (DD1 announcer / DD2 commentator):** about 150+ lines.
  - **Content:** generic callouts; **damage‑specific calls about your car** as in DD2 ("You've lost your hood!", "You're on FIRE!"); names and numbers.
  - **Anti‑repetition** (DD2's commentary was criticised as grating): no repeat within 3 minutes, variants for each line, and a frequency setting.
  - **Voice sources, in order of preference:**
    - (a) **you, or friends, record the lines** — authentic and free; I provide the script and a recording guide;
    - (b) a TTS voice whose **model card explicitly allows commercial use**. *Correction from v1: the original Piper repository was archived in October 2025, its successor piper1‑gpl is GPL; the tool would run offline only and not ship, and each voice's licence is checked individually;*
    - (c) a paid voice actor, with your approval.
  - **Processing:** all lines go through a **stadium PA chain** (band‑limit, compression, slap‑back, arena reverb).
- **Music:** in the spirit of the originals: **techno/jungle (DD1) and thrash metal/rock (DD2)**. Royalty‑free tracks *with your OK*, plus the game plays **your own OGG/MP3 files** from a `Music` folder. Intensity layers apply in the Bowl.
- **Mix:** ducking under the announcer and big crashes, a slow‑mo low‑pass, and separate volume sliders.
- **Audio gate:** a prototype at M2 and a client gate at M4.

---

## 13. UI / UX

- **Style:** condensed bold type (OFL fonts, *with your OK*), black and hazard‑yellow accents, scuffed metal and tape textures, broadcast lower‑thirds.
- **Fully keyboard‑navigable:** arrows, Enter and Esc work everywhere with visible focus, including car select, livery palette, rebinding and the replay controls. No screen requires a mouse.
- **Flow:** Title → Main menu (*Championship, Single Event, Time Trial, Practice, Options, Credits*) → Mode → Car select (a 3D **showroom** with stats and a Derby/Race hint) → Livery and number → Layout → Loading → Race → Results → Championship table.
- **HUD:**
  - **The DD damage diagram, lower right:** 4 zones and 4 wheels with 5 colour states and hit pulses; a lost wheel shows as an empty corner.
  - **Race info:** position, lap and lap times, points with popups, the leader beacon and tag, the wreck feed, speed and revs, "WRONG WAY", "PIT", countdown lights.
  - **Derby:** cars remaining, time, points strip.
  - **Championship:** a live table of your 5‑driver division with roof colours.
- **Results:** an animated points tally, the division table, and the promotion sequence.
- **Options:**
  - graphics presets and individual settings;
  - camera shake, slow‑mo, FOV, audio sliders;
  - **key rebinding with presets**, assist levels and the **Purist preset**;
  - the ruleset (v1.1);
  - a **keyboard ghosting test screen** (it shows which keys register when held together).

---

## 14. Controls (keyboard only — K7)

### 14.1 Default layout ("Arrows" preset)
| Action | Key | DD origin |
|---|---|---|
| Accelerate | ↑ | X button / A (PC) |
| Brake (reverses after stopping, as in DD2) | ↓ | Triangle (DD1) / Square (DD2) |
| **Reverse — instant, hold** (DD1 PS had a separate Reverse button; essential for reverse‑ramming) | Z | Square (DD1) |
| Steer left / right | ← / → | D‑pad |
| **Fast Steer** (hold: faster steering rate + more lock) | X | L1/R1 (DD2); "fast corner" (DD1 PC) |
| **Turn‑around** (a quick assisted 180° at low speed) | Space | L1+R1 (DD1 PS); Space (DD1 PC) |
| Change camera (4 views) | C | R1/R2 |
| Look back | V | — |
| Standings / division table | Tab | — |
| Pause | Esc or P | Start / P |
| Pit repair: select zone / mash repair | ← → / Enter or Space | X mash (DD2) |

**Ergonomics:** the right hand drives on the arrows; the left hand rests on **Z X C V + Space**. No default binding uses **Shift**.

**Presets** (the **first launch asks which one you want**):
- **"Classic DD PC"** — the layout you played, from the DD1 PC manual:
  - **A** gas;
  - **Z** brake/reverse (one key, as on PC);
  - **←/→** steer;
  - **Space + gas or brake** turn‑around;
  - **F2** cycles the 4 views;
  - **P** pause.

  We add **X** for Fast Steer and keep **Esc** as an extra pause key (DD2 PC paused with Esc).
- **Arrows** (modern): the table above.
- **WASD:** W/A/S/D drive; the right hand uses J (reverse), K (Fast Steer), L (camera) and Space.

### 14.2 Keyboard robustness
- **Sticky Keys:** five Shift presses open the Windows Sticky Keys dialog over the game, so Shift is avoided by default. If you bind Shift yourself, Options offers an opt‑in "suppress the Sticky Keys shortcut while playing". It uses Microsoft's documented technique for games, and the setting is **restored on exit**.
- **Key echo is ignored** (`InputEvent.IsEcho()`), so held keys never auto‑repeat. That keeps the pit mash honest (rate‑limited) and menus steady.
- **Ghosting:** the default combinations (for example ↑+←+X+Space, or ↓+→+Z) are tested on common keyboards, and the in‑game ghosting test screen lets you check yours.
- **No handbrake by default:** DD had none. Powerslides are done the DD2 way, by dabbing the brake while on the gas (↑ and ↓ can be held together). A "Handbrake" action can be bound optionally; it is off in Purist.
- **Simultaneous keys:** there are no exclusive key pairs, so ↑+↓ (brake‑dab), steering with Fast Steer, and reverse plus steering all work together.

### 14.3 Keyboard steering filter (critical for feel)
- **Steering response:** ramp‑in at 4–6 per second; **faster return to centre**; lock reduced at speed; Fast Steer raises both rate and lock (DD2).
- **Counter‑steer assist:** reduces the steering angle when the yaw rate and slip show oversteer. Levels: Off / Low / High (default Low; Purist Off).
- **Throttle:** a slight ramp, with light traction control for RWD V8s (default on, Purist off).

---

## 15. Software architecture

### 15.1 Structure
```
DestructionRacing/
  project.godot
  src/
    Rules/            # PURE C# library (no Godot dependency) — unit-tested
      Scoring.cs, SpinTracker.cs, WreckAttribution.cs, HitEnergy.cs, DamageMath.cs,
      Championship.cs, Divisions.cs, RulesConfig.cs (all tunables, per ruleset)
    Core/             # autoloads: Game, EventBus, Settings, SaveSystem, SceneLoader, Telemetry
    Vehicles/         # Car.cs (RigidBody3D), VehicleSim.cs, Wheel.cs, Drivetrain.cs, VelocityCache.cs,
                      # ICarInput, PlayerInput.cs (keyboard filter), PlayerProxyInput.cs, CarDefinition
    Damage/           # DamageModel.cs, CrushShapes.cs, DentLattice.cs, PartDetacher.cs, ScrapeMask.cs, DamageVfx.cs
    Modes/            # IGameMode, DerbyMode, WreckinRaceMode, StockCarMode, TimeTrialMode, PracticeMode
    AI/               # AIDriver.cs, RacingLine.cs, DerbyBrain.cs, Tactics/ (T1–T5), Personality.cs, ArenaDirector.cs
    Tracks/           # VenueDefinition, LayoutDefinition, TrackBuilder.cs, Checkpoints.cs, PitLane.cs, CrossoverZone.cs
    Cameras/          # Chase (DD2), ChaseFar (DD1), Bumper, InCar, LookBack, ArenaHigh, ReplayCams, CameraShake
    Audio/            # GranularEngine, ImpactAudio, Announcer, CrowdAudio, MusicPlayer
    UI/               # Menus (focus navigation), HUD, DamageDiagram, Popups, Results, Showroom, GhostingTest
    Replay/           # Recorder, Player, HighlightPicker
  shaders/            # car_paint, dent_lattice.gdshaderinc, glass, tyre, crowd, light_cone, post effects
  assets/cars/<id>/   # .glb + CarDefinition.tres     assets/venues/<id>/  # meshes, layouts, bakes per preset
  assets/{textures,audio,fonts,hdri}/
  tools/blender/      # templates/, morph_build.py, crush_bake.py, props, crowd, flipbooks, zebra_render.py
  tools/audio/       # granular_prep.py, announcer_build.py, normalize.py
  tools/sim/          # headless sim runner (AI + PlayerProxy), metrics + frustration report
  tests/              # xUnit (Rules), Godot headless scenario tests (incl. DD Code T1–T5)
  docs/               # reference_board.md, reference_feel.md, fidelity_checklist.md
  CREDITS.md
```

### 15.2 Key design rules
- **Rules are separate from presentation.** Event bus events (`CarHit`, `CarSpun`, `CarWrecked`, `LapCompleted`, `PitEntered/Exited`, `CarEliminated`, `RaceFinished`) drive the modes, HUD, audio and announcer.
- **Data‑driven:** cars, venues and layouts, divisions, drivers and all tunables are Resources or JSON.
- **Physics:** Jolt at **120 Hz**, CCD on cars, tuned solver iterations, capped angular velocity, and a velocity cache for hit energy (§5.3).
- **Dev tools:** a live tuning panel (F1, dev builds), and local telemetry CSV per session.

### 15.3 Vehicle simulation (arcade, weighty)
- **Chassis:** a `RigidBody3D` with a compound collider and a lowered, tunable centre of mass (rollovers on big side hits, ramps and kerbs, as in DD2; none in normal cornering).
- **Wheels:** 4 sphere shape‑casts with spring‑damper suspension; a slip‑angle tyre curve with a friction circle; surfaces asphalt, concrete, dirt, grass and mud.
- **Drivetrain:** a torque curve, a 4/5‑speed automatic (RPM for audio), RWD or FWD.
- **Spin calibration:** `SPIN_ASSIST` (extra yaw on rear‑quarter hits, by lever arm) and `POST_HIT_GRIP_LOSS` (about 0.4 s), tuned to the reference feel sheet.
- **Turn‑around:** an assisted yaw impulse at low speed (DD1).
- **Air and landing:** minimal air control; landings damage the suspension.

### 15.4 Cameras (4 in‑game views, as in DD)
1. **Chase (default):** the DD2 PC framing, close, low and level. It stays at a fixed distance, the heading swing is smoothed, with a light speed FOV and shake.
2. **Chase far:** the DD1 PC framing, further back and looking slightly down. Both framings are measured in `docs/reference_feel.md`.
3. **Bumper.**
4. **In‑car:** a stripped derby interior with roll cage, dash and steering wheel; a shared kit per template.

**Also:** Look back, and "Arena high" in the Bowl. The camera always stays upright during rollovers.

### 15.5 Replays
- **Recording:** 30 Hz transforms, wheels, inputs, damage events (re‑applied deterministically) and debris.
- **Playback (DD2‑style):** preset TV angles with automatic cuts to high‑energy action, slow‑mo (0.25×/0.5×), chase and free cam.
- **Highlights reel:** of the top 3 moments (*Should*).

---

## 16. Performance targets and budgets

| Target | Resolution / preset | Frame rate |
|---|---|---|
| Dev PC (RTX 5090) | 4K Ultra | ≥ 120 fps |
| Mid‑range (RTX 3060 / RX 6600) | 1080p High | ≥ 60 fps (1% lows ≥ 50) |
| Minimum (GTX 1660) | 1080p Low + FSR | ≥ 60 fps |

**Budgets (mid‑range, 20 cars + 150 debris):**
- **CPU:** physics ≤ 3.5 ms; AI ≤ 1 ms; deformation ≤ 0.5 ms (amortised).
- **Cars:** LOD0 ≤ 60k triangles, LOD1 15k, LOD2 4k.
- **Lights and particles:** ≤ 6 shadowed lights; ≤ 40k particles.
- **Memory:** ≤ 6 GB VRAM on High.

**Stress scene:** a 20‑car pileup in the Bowl with a frame‑time capture, run at every milestone.

---

## 17. Quality assurance and verification

### 17.1 Automated
- **Rules unit tests:** scoring, ×2, spin tiers and attribution (recorded traces, incl. multi‑body pileups), wreck attribution, championship promotion and swaps, tie‑breaks.
- **Scenario tests (headless):**
  - **DD Code T1–T5**, each ≥ 60% success;
  - a stable 20‑car pileup (no explosions or NaNs);
  - the derby always ends;
  - the AI completes 100 laps on every layout in both directions without getting stuck.
- **PlayerProxy bot:** drives through the **same keyboard input path** as a human (digital keys → steering filter).
  - **Human‑like timing:** a 150–250 ms reaction delay, aim noise and occasional mistakes.
  - **Profiles:** "average returning fan" and "skilled".
  - **What it measures:** all *player* metrics in the sims.
- **Simulation reports:** 200 events per mode, accelerated, with AI plus PlayerProxy. They cover engagement, frustration and balance.
- **Visual capture:** scripted beauty shots, zebra renders and a gameplay clip (ffmpeg) at every milestone. I review them, and you see them at the gates.

### 17.2 Fidelity checklist (K3) — `docs/fidelity_checklist.md`
- **Rules:** every rule in §4–§6 and every DD Code tactic, ticked when implemented and tested.
- **Additions:** every non‑DD addition, with its §3 class and Purist behaviour.
- **Reference feel:** the measured reference feel values next to ours.

### 17.3 Beauty checklist (K1/K2)
- **No visual defects:** no placeholder art, no z‑fighting, no LOD popping, no fog ghosting, no surface wobble in zebra renders, and a readable HUD at 1080p.
- **The hero shots read as DD:** the Bowl by the mountains on a sunny afternoon, a smoky pileup, a battered car with a big number and a division roof.
- **Damage looks like metal, not rubber.** It is judged against the Wreckfest references at every art gate.

### 17.4 Engagement and frustration targets (K4)

**Definitions:**
- **Hit:** car‑car contact with E ≥ E_HIT. That is calibrated so a deliberate ram at ≥ 20 km/h closing speed counts and a scrape doesn't.
- **Attack on X:** an AI targeting X that lands a hit within 5 s.
- **Crossover incident:** a hit inside the crossover zone between cars on different legs.
- **Close battle:** two cars within 0.5 s of each other for ≥ 10 s.
- **Deliberate hit:** in the 0.5 s before the hit, the player had throttle on and was heading at the victim (closing angle < 45°).
- **Idle stretch:** time with no hit anywhere, measured only while ≥ 5 cars remain.

**Targets**, measured with PlayerProxy ("average fan", Normal) in 200 sims, and confirmed with your telemetry at playtests:

| Mode | Engagement metric | Target |
|---|---|---|
| Derby | Time to first contact | < 3 s |
| Derby | Longest idle stretch (≥ 5 cars left) | ≤ 8 s |
| Derby | Player hits per minute (median) | ≥ 8 |
| Derby | Event length (median) / player survival (median) | 2–4 min / 60–150 s |
| Derby | Share of player points from deliberate hits | ≥ 70% (skill beats luck) |
| Derby | Dominant strategy check: "hide", "pure ram", "reverse only", "spin‑farm" bots | none wins > 40% of events |
| Wreckin' | Spins in the field per lap | ≥ 3 |
| Wreckin' | Crossover incidents per race (figure‑8) | ≥ 2 |
| Wreckin' | Player position changes per race | ≥ 6 |
| Wreckin' | Leader spun per race | ≥ 2 |
| Wreckin' | Racer vs. hunter bot at equal skill | points within ±15% |
| Wreckin' | Cars within 3 s of the player | ≥ 60% of race time |
| Stock Car | Spread from 1st to 10th on lap 2 | ≤ 6 s |
| Stock Car | Close battle involving the player in the last 2 laps | ≥ 50% of races |
| Stock Car | DNFs per race (bruisers still bite) | 1–4 |
| Time Trial | Gold medal | reachable by "skilled", not by "average" |
| Championship | Seasons already decided before the Bowl finale | ≤ 30% |
| All | Restart to driving / load time | ≤ 3 s / ≤ 5 s |

| Frustration metric | Target |
|---|---|
| Player wrecked within the first 30 s / 60 s of a derby | ≤ 5% / ≤ 15% of derbies |
| Player wrecks with ≥ 3 simultaneous attackers | ≤ 10% of player wrecks |
| Player stuck or recovering | ≤ 5% of event time |
| Player wrecked before half distance in Wreckin' | ≤ 10% of races |
| Promotion attempts needed (median, per division) | 1–2 |

**Client playtest gates:** M1 (feel), **M1b (grey‑box derby)**, **M1c (grey‑box Wreckin')**, M4 (vertical slice), M6 (racing modes).

---

## 18. Milestones (each ends with a demo build and an acceptance check)

| M | Name | Contents | Acceptance |
|---|---|---|---|
| **M0** | Foundation and references | Install Godot .NET; git repo; skeleton; build, test, run and screenshot scripts; Rules library + tests; **check that the LightmapGI bake can run unattended**; **reference board**; **reference feel sheet** from DD1/DD2 footage (watched, not downloaded, *with your OK*); **fallback car‑model shortlist**; your answers to the §22 fidelity questions | Tests green; the app launches; a screenshot is captured; docs written |
| **M1** | Driving feel (grey box) | Vehicle sim, keyboard filter, Fast Steer, turn‑around, reverse, the 4 cameras, debug panel | **You drive it and approve** |
| **M1b** | **Grey‑box Destruction Derby** | Hit energy, contact, spin calibration, SpinTracker, scoring, mechanical damage and the wreck rule, a simple Derby AI, the HUD damage diagram, PlayerProxy and the sim harness, DD Code scenario tests | **You play it; first derby metrics; T1–T5 pass** |
| **M1c** | **Grey‑box Wreckin' Racing** | Grey‑box figure‑8 with crossover, basic racing AI, laps, finishing bonus, leader ×2 and beacon | **You play it; first Wreckin' metrics** |
| **M2** | Art and audio spike | Template + morph pipeline; 3 cars (Kitsune, Brougham, Nordvik) with zebra checks; paint shader; **layered deformation** (crush shapes + dent lattice + rest‑pose sampling); the Bowl's look with a baked afternoon preset; a fog‑ghosting test with a moving camera; granular engine prototype | **Kill criterion: you rate cars and damage at Wreckfest level or better — otherwise switch to purchased models** |
| **M3** | Full damage and VFX | Detachable parts, glass, lights, smoke, fire, sparks, debris, scrape mask; pileup stability | Tests green; damage looks like metal |
| **M4** | **Vertical slice: Destruction Derby** | Final Bowl art (3 presets), crowd, full Derby AI and director, final HUD, announcer v1, audio v1, results, basic replays | **Derby engagement and frustration targets met; you playtest; audio gate** |
| **M5** | Car roster | Up to 9 cars (cut line 6), liveries, 19 drivers, balance sims | Balance targets met; roster approved |
| **M6** | Racing | Crossroads Speedway final art: Oval + Figure‑8 (+ Infield course if on schedule), full racing AI with DD Code, Wreckin', Stock Car, Time Trial + ghost, pits + mini‑game, rollovers, wrecks as obstacles | Racing targets met; you playtest |
| **M7** | Championship and front end | Divisions, roof colours, promotion and swaps, flow, text taunts, keyboard‑navigable menus, showroom, options, rebinding presets, ghosting test, save and load; *Should:* Total Destruction, Duel | A full championship playable from start to finish |
| **M8** | Polish | Replays (+ highlights), final audio mix, music, the full announcer set, VFX and post polish, performance, LODs, Purist preset | Performance and beauty checklists pass |
| **M9** | Release v1 | Windows export, zip, CREDITS and licences, final QA | Release checklist green |

The order is **fun before art**: M1–M1c prove the gameplay on keyboard. M2 then proves that beauty is achievable, with a fallback, before the expensive content work in M3–M6.

---

## 19. Scope (explicit cut line)

| Priority | Contents |
|---|---|
| **Must (v1)** | The Bowl (3 lighting presets); Crossroads Speedway **Oval + Figure‑8** (forward/reverse, 3 presets); Destruction Derby, Wreckin' Racing, Stock Car Racing, Time Trial, Practice, Championship (Standard ruleset); 20‑car field; **9 cars (3 EU / 3 JP / 3 US), cut line 6 (2/2/2)**; 19 drivers with text taunts; full damage; announcer; replays; keyboard only with presets |
| **Should (v1 if on schedule)** | Infield road‑course layout; Total Destruction; Duel; replay highlights reel |
| **Later (v1.1+)** | DD1 Classic ruleset; hot‑seat Multi‑Player; voiced taunts; the remaining 3 cars; Quarry Run; DD2‑style arenas; city and seaside tracks; rain; 2‑player split‑screen |
| **Out** | Gamepad (easy to add later), online play, track editor, mods, consoles |

Compared with the scope you agreed ("more or less"), v1 adds the Stock Car and Time Trial modes, the championship and a full 20‑car field. All of them are core DD; the field size matches both originals.

---

## 20. Reference: original DD1/DD2 gameplay facts

### 20.1 Facts used in this plan
**Sources:**
- **Primary (read directly, PC versions):**
  - the **DD1 PC (DOS) manual**: an English re‑translation from dosdays.co.uk;
  - the **DD2 PC manual**: English scan, OCR partly garbled, and a Portuguese translation (BIGMAX) on archive.org.
- **Manuals, also read directly:** the DD1 PlayStation Classic manual (separate Brake/Reverse buttons, the five‑point "Destruction Derby Code", Stock Car bashers).
- **Guides and wikis:** Law667's DD1 FAQ (GameFAQs), destructionderby.fandom.com, Wikipedia (Retro Gamer #163).
- **Other sites:** DOSDays, psxdatacenter.

| Topic | DD1 (1995) | DD2 (1996) |
|---|---|---|
| Versions | **PC (you played this):** 16 cars, 4 leagues × 4 drivers, hot‑seat up to 16, network link modes. **PS:** 20 cars, 5 divisions × 4 | **PC (you played this):** 4 leagues × 5 drivers = 20 cars, hot‑seat you + 9 |
| Modes | Wreckin', Stock Car, Destruction Derby (the Bowl only), Time Trial; Practice, Championship, Multi Player, Duel, Total Destruction (all cars hunt you, against the clock); link: Duel, Team Pairs, Tag, Seek & Destroy | The same 3 styles + Time Trial, 4 arenas ("cause as much damage as possible to opposing cars"), Total Destruction, hot‑seat |
| Scoring | **PC manual:** "Destruction Derby and Wreckin' mode points": wreck 10, 360° 10, 180° 4, 90° 2. **"Your points are doubled if your opponent's car was in the lead when you attacked it."** Max 99 per race. Wreckin' bonus 10/6/4 and Stock Car 50…0 (FAQ). The Bowl: last car running wins (psxdatacenter) | **PC manual:** "Points in Wreckin' Racing and Destruction Derby": wreck 25, 360° 50, 180° 25, 90° 10. **"All points are doubled if you damage the car in 1st place."** Downed car 0; cap 999 (fandom). Finishing points undocumented |
| Race end | When the first car finishes (fandom) | When the player finishes or is wrecked (fandom ✱) |
| Championship | **PC:** roofs Div 4 yellow, Div 3 blue, Div 2 red, Div 1 black; Wreckin' season ends with a Bowl event; no promotion → repeat the tracks. (PS: 5 divisions incl. white, and demotion) | **PC:** 4 × 5, start Div 4; Wreckin' 5 races, the last in the arena; Stock Car 4 races; promotion adds a new track and a new arena; no promotion → repeat the tracks |
| Laps | 8–12 | 5–10 |
| Damage | Diagram lower right. Front → radiator → stop. Sides → pull. Tough rear. Wrecks stay. No flips, no detached parts | Parts fly, wheels lost on heavy rear damage, fire. **Pits (PC manual):** auto‑guided to the garage, ←/→ around the car, mash Enter to repair, 5 s limit; Wreckin' once, Stock Car as often as you like |
| Physics | Flat plane, powerslides; Rookie auto‑rights; turn‑around | Rolls and cartwheels; righted after a time penalty (manual); slippery |
| AI | 19 personalities with taunts; bashers in Stock Car; too easy | Aggressive, criticised as unfair; the PC patch toned it down |
| Tracks | Small and narrow; oval, figure‑8 with blind crossover, seaside, 4‑crossroads track, city, bonus ruins; the Bowl by the mountains | Wide; jumps, tunnels, street grid, night city, figure‑8; hump, Colosseum, pit and crusher‑drop bowls |
| Presentation | 4 views incl. in‑car; replay editor; announcer + taunts; techno/jungle | Preset replay angles + slow‑mo; damage‑specific commentary; thrash metal |
| Controls | **PC:** A gas, Z (Y on German keyboards) brake/reverse, arrows steer, Space + gas or brake = turn‑around (beginner cars turn automatically), F2 = next of 4 views, P pause; joystick and mouse optional. *PS: Brake and Reverse on separate buttons, L1+R1 turn‑around* | **PC:** Esc pause, S slow‑mo in replays, arrows and Enter in menus and pits; full in‑race key list not legible in the scan. *PS: Fast Steer on L1/R1* |
| Praised | Smashing, "thinking man's" strikes, the Bowl, crossover pile‑ups, leader‑doubling, personalities, 20 cars | Wide tracks, jumps, flying parts, several Wreckin' strategies |
| Criticised | Narrow tracks, too easy, short‑lived, samey tracks, camera too close | Unfair AI, repetitive commentary, twitchy steering after crashes, shallow, opponents vanish |

**The criticisms → our fixes:**
- **Narrow tracks:** 3‑abreast layouts.
- **Too easy / unfair AI:** division scaling, attention budget, frustration metrics.
- **Samey tracks:** layouts × directions × lighting presets with distinct grades, and new venues in v1.1.
- **Camera too close:** camera options.
- **Repetitive commentary:** 150+ lines, cooldowns, a frequency setting.
- **Twitchy steering after crashes:** smooth damage pull, keyboard filter.
- **Opponents vanish:** short laps, pack density.
- **Shallow:** the DD Code tactics, rivals, Time Trial medals.

### 20.2 Assumptions and tunables (✱)
| # | Unknown | Assumption (default) | Reason / owner |
|---|---|---|---|
| A1 | DD2 Wreckin' finishing bonus | 25/15/10 (DD1 ×2.5) | Keeps DD1's ratio |
| A2 | DD2 Stock Car finishing points | DD1 table 50…0 | The only documented table |
| ~~A3~~ | Spins scoring in the Bowl | **Resolved: spins score** | Both PC manuals list the table under Destruction Derby *and* Wreckin' |
| A4 | The ×2 "leader" in the Bowl | Event points leader | Mirrors "in first place" |
| ~~A5~~ | Who must lead for ×2 | **Resolved: the car you damage** | DD1 PC manual and DD2 PC manual agree |
| A6 | Promotion threshold | 1st in the division | DD1 FAQ |
| A7 | Hitting a wrecked car in DD1 | 0 | DD2 rule |
| A8 | Bowl time limit | None | None found |
| A9 | Immobilised = wrecked after 10 s | Yes | Prevents stalls |
| A10 | Championship tie‑break | More wrecks, then best finish | Not documented |
| A11 | DD2 race end | Continues until the player finishes or is wrecked | Fandom only |
| ~~A12~~ | DD2 pit limits | **Resolved:** Wreckin' once, Stock Car as often as you like, 5 s each | DD2 PC manual |
| A13 | DD2 flip righting | Auto‑righted after a time penalty | DD2 PC manual OCR shows "IF YOUR CAR FLIPS" plus a penalty; the exact rule is not legible |
| A15 | Survivor bonus in the Bowl | DD1 10 / DD2 25 | Fandom only (DD1); not in either PC manual |
| A14 | Zone hit points / damage curve | `k·(E−6 kJ)^0.8`, compressed above 25 and capped at 45 per hit and zone (a front survives at least 2 of the biggest crashes); tuned with the M1b derby sims (`RulesConfig`) | Not documented |
| A16 | Spin hit threshold | 3 kJ (≈ 11 km/h closing): taps score spins, as in DD | Reference feel: a rear‑quarter tap spins the leader |
| A17 | Grinding and scraping | Repeat contact of the same pair within 0.8 s deals 30%; sliding contact above 20 km/h grinds the touching zone; the squeezer gets the credit (DD Code T4) | Keeps pile‑ups from stripping every car, makes squeezes pay |
| A18 | Damage on a destroyed zone | Half of it reaches the front (engine) | Without it, cars with destroyed sides and rear could never be finished off |
| A19 | Opening crunch and first minute | Derby damage starts at ×0.5 at GO and ramps to ×1 over 75 s (calibration; M1b had ×0.6 over 60 s, re-checked with reproducible sims in M1c) | Frustration targets: player wrecked in 30 s ≤ 5%, in 60 s ≤ 15% |
| A20 | DD2 Fast Steer at derby speeds | Loosens the rear and adds yaw torque below ~80 km/h, so sharp turns (DD Code T5) are possible | The DD2 manual only says "fast steering" |
| A21 | Race damage | All damage in a race is ×0.5 (`RaceDamageScale`, calibration) | At ×1 the PlayerProxy was wrecked before half distance in 83–100% of races (target ≤ 10%): race contact is constant and incidental, the derby curve assumes deliberate rams |
| A22 | AI pace in Division 4 | The racing AI drives at ×0.97 of its skill's pace (division scaling, §10.5) | At ×1 the AI racers out‑pace an average player, so only hunting paid; at ×0.97 racer and hunter bots score within ±15% |
| A23 | Crossover and wall ends | The walls open where the figure‑8 legs cross; where a wall resumes it is flared (≈8°) and the four corners are closed | Blunt wall ends at the opening wrecked cars that drifted into the gap; DD1 footage shows no detail |
| A24 | Race end | The race ends when the player finishes; if the player is wrecked, when the winner takes the flag. Everyone else is classified by distance | Not documented; keeps "skip to results" short |

---

## 21. Risks and mitigations

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| Cars don't look premium | K1 | Medium | Template + morph, zebra QA, the hardest car first, **an M2 kill criterion with a pre‑scouted purchase fallback** |
| Damage looks rubbery | K1 | Medium | Layered crush shapes + fine dent lattice + rest‑pose sampling; judged at M2 |
| Gameplay isn't fun, or isn't DD | K3/K4 | Medium | **Grey‑box gates M1b/M1c before art**, DD Code scenario tests, reference feel sheet, PlayerProxy metrics |
| Keyboard handling is twitchy | K7 | Medium | Steering filter, Fast Steer, assists, M1 gate |
| Sticky Keys / ghosting / key echo | K7 | Medium | No Shift by default, opt‑in suppression with restore, echo filtering, ghosting test screen |
| Hit detection is wrong in pileups | K3/K4 | Medium | Our own hit energy from cached velocities (not Jolt impulse estimates); pileup unit traces |
| Fog and smoke ghosting | K1 | Medium | Short‑lived lights with fog energy 0, mesh light cones, particle smoke, moving‑camera test at M2 |
| Lightmap baking can't be automated | Schedule | Low–Med | Checked in M0; fallback: you click Bake, or VoxelGI/SDFGI per preset |
| Audio sounds synthetic | K1 | Medium | Granular engines from real recordings; voice recorded by you or a licensed voice; audio gate at M4 |
| Licensing (TTS voice, assets) | Legal | Low–Med | Per‑asset licence check, CREDITS.md, your approval for anything non‑CC0 |
| AI dumb or unfair | K4 | Medium | Utility AI, personalities, attention budget, frustration metrics |
| Pileup instability | K1 | Medium | 120 Hz, CCD, angular velocity caps, stress scene every milestone |
| Rule facts uncertain | K3 | Low–Med | ✱ list, your answers (§22), all values tunable |
| Scope creep | Schedule | Medium | Must / Should / Later cut line |
| IP or trademark | Legal | Low | Fictional names and brands, original tracks, no DD assets |

---

## 22. Decisions needed from you

**Fidelity — answered on 2026‑10‑14:**
1. ~~PlayStation or PC?~~ → **PC.** The PC manuals are now the reference: Standard = DD2 PC (20 cars); DD1 Classic = DD1 PC (16 cars); a "Classic DD PC" key preset.
2. ~~Spins in the Bowl?~~ → "Whatever the original did" → **spins and wrecks both score in the Bowl** (both PC manuals).
3. ~~When were points doubled?~~ → "Find out" → **when the car you damage is in 1st place** (both PC manuals agree).

**Production:**

4. **Download approvals:** each is listed with its source and size before downloading. They are:
   - Godot 4.7.2 .NET + export templates;
   - CC0 textures and HDRIs;
   - CC0 or royalty‑free audio;
   - OFL fonts;
   - royalty‑free music;
   - optionally a TTS tool and voice;
   - viewing DD gameplay videos for the reference feel sheet.
5. **Announcer voice:** would you (or friends) record the ~150 lines (recommended), or should we use a licensed TTS voice or a paid voice actor?
6. **Music:** royalty‑free tracks, your own files, or both?
7. **Target hardware:** just your PC, or hold to the mid‑range target in §16 (recommended)?
8. **Final game name** (working title: "Destruction Racing").

---

## 23. Review log

### 23.1 v2.1: your answers and the PC manual check
| Question | Your answer | What the PC manuals say | Plan change |
|---|---|---|---|
| Version played | PC | DD1 PC: 4 leagues × 4 = 16 cars, A/Z/arrows/Space/F2/P keys. DD2 PC: 4 leagues × 5 = 20 cars, pits once (Wreckin') or unlimited (Stock Car), 5 s each | The PC versions are the reference; the "Classic DD PC" key preset is offered at first launch; DD1 Classic uses 16 cars; roof colours follow the DD1 PC scheme |
| Spins in the Bowl | "Whatever the original did" | DD1 PC: "Destruction Derby and Wreckin' mode points are distributed as shown". DD2 PC: "Points in Wreckin' Racing and Destruction Derby are:" | **Spins and wrecks both score in the Bowl** (v2 had wrecks only, based on a fan wiki); a spin‑farm bot was added to the dominant‑strategy sims |
| Doubling rule | "Find out" | DD1 PC: "doubled if your opponent's car was in the lead when you attacked it". DD2 PC: "doubled if you damage the car in 1st place" | **The car you damage must be leading**, confirmed for both games |

### 23.2 v2: independent review of v1

**Review:** an independent reviewer with a fresh context (game director and technical director perspective), focused on the key guidance. It gave these scores for plan v1:

| Item | Score |
|---|---|
| K1 beautiful and polished | 2.5/5 |
| K3 same gameplay | 3/5 |
| K2 style | 3/5 |
| K5 cars | 4/5 |
| K6 engine | 4/5 |
| K8/K4 scope and engagement | 2.5/5 |
| K7 keyboard | 3/5 |

Its claims about the DD1 manual (separate Reverse, the five tactics, Stock Car bashers) were **verified against the PS manual** before being used.

| # | Finding (severity) | Disposition | Where |
|---|---|---|---|
| C1 | Fun is validated too late; the M1 gate has no contact (Critical) | **Accepted.** Added the grey‑box M1b (derby) and M1c (Wreckin') client gates before the art spike | §18, §17.4 |
| C2 | A lofted procedural car generator won't look premium; no fallback trigger (Critical) | **Accepted.** Template + morph, zebra QA, hardest car (Kitsune) in the spike, M2 kill criterion vs. Wreckfest, pre‑scouted purchase shortlist in M0 | §8.6, §18 |
| C3 | A coarse lattice gives rubbery dents; hinged parts drag (Critical) | **Accepted.** Layered: baked crush shapes per zone and severity + a 24×12×48 dent lattice + rest‑pose attribute | §6.2 |
| M1 | DD2 Bowl default rewards spin‑farming and changes the core derby (Major) | Accepted in v2 (wrecks only). **Superseded in v2.1:** the PC manuals show spins *do* score in the Bowl, so fidelity wins; "last car running wins" and the dominant‑strategy sims (now incl. spin‑farming) remain | §4.0, §4.1, §22 |
| M2 | Uncertain facts presented as confident; PS vs. PC (Major) | **Accepted.** Added ✱ (A11–A13); PS/PC question. *Adapted:* the ×2 "victim in 1st" rule stays the default because the DD2 manual states it; only DD1 is ambiguous | §4.0, §20, §22 |
| M3 | Metrics measure activity, not the player; undefined terms; the 8 s idle rule contradicts duel pacing (Major) | **Accepted.** PlayerProxy bot, frustration metrics, operational definitions, idle rule only with ≥ 5 cars, dominant‑strategy bots | §17 |
| M4 | The manual's "Destruction Derby Code" tactics are missing; Stock Car AI too clean (Major) | **Accepted (verified in the manual).** New §4.7 with T1–T5 as requirements + scenario tests; AI uses and falls for them; Stock Car bruisers keep bashing | §4.7, §10 |
| M5 | SDFGI doesn't take dynamic contributors; fog ghosting; bake automation (Major) | **Accepted.** LightmapGI per preset + probes; ghosting mitigations and a moving‑camera test; bake automation checked in M0 with fallbacks | §8.1, §18 |
| M6 | Jolt contact impulses are estimates in pileups (Major) | **Accepted.** Hit energy from cached pre‑solve velocities, used for every threshold | §5.3 |
| M7 | Sticky Keys, missing dedicated reverse, overloaded Space, key echo, ghosting, presets, menus (Major) | **Accepted.** New key layout (no Shift, Z reverse, X Fast Steer, Space turn‑around only, no default handbrake), echo filtering, ghosting screen, DD1 PC preset, keyboard‑only menus and palette | §14, §13 |
| M8 | Synthesised audio and TTS threaten polish; outdated Piper licence (Major) | **Accepted.** Granular engines from real recordings; voice by you or a licensed voice; Piper corrected; audio gate | §12 |
| M9 | Scope has grown; a single track gets samey (Major) | **Accepted with adaptation.** Must/Should/Later cut line; DD1 Classic, hot‑seat and voiced taunts → v1.1; Quarry Run → v1.1, replaced by Crossroads layouts (Oval + Figure‑8 Must, Infield Should). *Adapted:* 9 cars (3 per region) with a cut line at 6, instead of a flat 6–8, so the regional mix stays balanced | §19, §7.3, §9.1 |
| m1 | Additions on by default break §3's own rule (Minor) | **Accepted.** All listed in the fidelity checklist; Purist preset; spin calibration explained as calibration, not an addition | §3 |
| m2 | §9.3 balance target contradicts the suitability hint (Minor) | **Accepted.** "±15% in the intended mode; no car top‑3 in all modes" | §9.3 |
| m3 | Division sizes with no relegation undefined (Minor) | **Accepted.** Explicit AI swap rules | §4.5 |
| m4 | DD1 roof colour cyan, not blue; white for Div 5 (Minor) | Accepted in v2. **Superseded in v2.1:** that was the PS version; the PC manual gives Div 4 yellow, Div 3 blue, Div 2 red, Div 1 black (4 divisions) | §4.0 |
| m5 | Inconsistent lap lengths (Minor) | **Accepted.** 35–55 s per layout | §4.2, §7.3 |
| m6 | Scrape mask 512² too small (Minor) | **Accepted.** 1024² | §6.2 |
| 1c | Night as the hero look departs from DD's daylight look; no reference board; Wreckfest not mentioned (Score note) | **Accepted.** Afternoon and golden‑hour hero presets, night as a variant; reference board incl. Wreckfest | §7 |
| F | C# hot reload claim is unreliable; trade‑offs unstated (Factual) | **Accepted.** Claim removed; trade‑offs listed | §2.1 |
| F | DD2 multi‑player "up to nine" (Factual) | **Noted** (sources differ: 9 vs. 10); irrelevant to v1 since hot‑seat moved to v1.1 | §20.1 |
