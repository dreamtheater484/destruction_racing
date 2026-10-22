# Progress

## M0 Foundation and references — done (2026‑10‑14)
- **Engine:** Godot 4.7.2 .NET and export templates installed (both SHA‑512 verified against the official release).
- **Repo:** git repo initialised (nothing committed yet); offline NuGet config.
- **Rules library** (`src/Rules`): rulesets Standard (DD2 PC) and DD1 Classic (PC), scoring, spin tracker, hit energy, wreck attribution, damage zones and the wreck rule, championship. **58 unit tests passing.**
- **Dev tooling:** build/test/run/capture/bake scripts. Automated screenshots work.
- **Unattended LightmapGI baking works** (`addons/autobake`), so plan §8.1 needs no fallback.
- **Docs:** `docs/reference_board.md` and `docs/fallback_car_models.md`.
- **Not done yet:** the reference feel sheet (needs your OK to watch DD gameplay videos).

## Post-M8: when you're wrecked in a race — ready for your playtest (2026‑10‑22)
Your report: wrecked in a race, you pressed skip, and your wrecked car seemed to race on, pit and finish, sped up.
- **What it was:** the car on screen wasn't yours. After a wreck the race put the chase camera behind the nearest car (often the one that wrecked you, damaged and heading for the pits), so it looked exactly like driving. The skip ran the race "×8", which on your PC was slower than real time (4 fps). It also made every physics step 8 times longer (15 Hz instead of 120 Hz), so the rest of the race was simulated sloppily.
- **Now, in a race:**
  - A TV camera shows your wreck: **WRECKED – DNF** and "RESULTS IN 5 · ENTER straight to the results · SPACE watch the race".
  - After 5 s (or ENTER) the rest of the race is simulated at full speed, with the 3D view and the sound off: "FINISHING THE RACE · THE LEADER IS ON LAP 5/8". Then the results, with your wreck behind them.
  - It runs at about 7× real time on your PC: wrecked halfway, about 15 s; wrecked on lap 1, under 30 s. It is the same simulation as when you watch, so the results and the championship points are real.
  - **SPACE** (or ←/→) watches the race instead, on the TV cameras, with the car named: "WATCHING #24 PRICE · ←/→ another car · T TV / chase · SPACE ×2 speed · ENTER straight to the results".
- **The derby:** ENTER uses the same fast finish ("FINISHING THE DERBY · 6 CARS LEFT"). Watching on the TV cameras stays the derby's default, as you approved it in M4.
- **×2 speed** (watching) made every physics step twice as long too. Every speed-up now keeps the 1/120 s step: the game runs more steps instead.
- **Black squares in the TV cameras,** found while testing this. Godot's depth of field breaks up into black squares on a multisampled (MSAA) frame, around distant cars and their lamps. That affected the TV cameras, the replays and the highlights on the Medium, High and Ultra presets. While a camera with depth of field is on screen, MSAA now gives way to SMAA, and your MSAA comes back with the chase camera. Before, 25 of 57 TV frames had black squares; after, none.
- **Checks:**
  - The replay self-test after a fast finish: 20 of 20 cars rebuilt bit for bit, in a race and in a derby.
  - Unit tests: 102 of 102 pass. The pileup test passes.
  - The sims are unchanged, because none of this runs headless.
- **Found on the way, flagged as separate fixes:** the headless sims no longer give the same race twice for the same seed (already so before this change; the statistics are unaffected); and the derby stalemate (two cars circling each other), which is in progress.

## Post-M8 handling: steering and race AI — ready for your playtest (2026‑10‑19)
Your playtest of the championship: the cars were very hard to steer and went into the walls, and the opponents were slow off the line (some even reversed) and slow in general. DD2 is the reference here, DD1 the second one.
- **Play it:** double-click **Play Destruction Racing.cmd**, as before. Your settings and championships carry over.
- **What DD2 does** (measured from the DD2 PC video, details in `docs/reference_feel.md`):
  - DD2's cars corner at a steady **2.2–2.4 g** from 100 to 170 km/h, and the turn follows the keys within about 0.1 s. Ours managed **0.8 g**, the grip of a road car. At 130 km/h they kept turning for 2.2 s after the key came up, and at 150 km/h they spun. That was the "into the walls".
  - Your Chalk Canyon video: the car holds 170–230 km/h through the canyon corners (300–344 km/h on the straights). Its turn rate can't be measured reliably, because the canyon walls hide the sky, but it confirms that DD2's grip holds at very high speed.
  - **Wrecks:** DD2's race HUD counts the cars still running. In the video's four races, 0, 5, 9 and 5 of 20 cars were wrecked (4.75 on average), mostly in the second half; only one race lost cars before half distance.
- **The steering** (✱ A33): above 50 km/h the tyres grip more, up to ×2.3 from 108 km/h. Above 40 km/h a held key is a steady turn at the rate the lock asks for, and letting go straightens the car. A hit, a spin, Fast Steer and reverse still work as before, and so does everything below 40 km/h (the derby's shoving, turning round). It is calibration, so it's on in Purist too.

  | Speed | Turn rate (key held) | Cornering | Stops turning after the key comes up |
  |---|---|---|---|
  | 80 km/h | 39°/s | 1.55 g | 0.27 s |
  | 130 km/h | 36°/s | 2.27 g | 0.26 s (was 2.2 s, at 1.1 g) |
  | 175 km/h | 26°/s | 2.27 g | – |
  | 208 km/h | 22°/s | 2.27 g | – |

  - **The brake-dab powerslide** (DD2's manual): braking in a turn loosens the car. A 0.4 s dab at 130 km/h raises the turn from 40 to 57°/s and the slide from 4° to 11°, then the car settles back into the turn without spinning.
- **The opponents:**
  - **The start:** the AI drivers were already thinking during the countdown, so their "stuck" check fired, and some reversed at GO. They now wait for the green. The rows behind also waited for the car in front to pull away, like a queue at a traffic light; now they see it accelerating. All 20 cars are on full throttle at GO and at about 100 km/h after 2 s (DD2: 0–100 km/h in 2.2 s).
  - **Their pace:** the AI's speed plan now uses the same grip as the cars: 131 km/h through Crossroads' 70 m bends, where a car can hold about 143 km/h. The base pace is ×1.0 in Division 4 up to ×1.045 in Division 1 (was ×0.97 to ×1.015, ✱ A22, A25), and the rookies are a little closer to the racers.
  - **Best laps on the oval:** the AI racers 21.1 s and the rookies 22.7 s in traffic, against the average bot's 23.9 s. Alone, the skilled bot laps in 20.2–21.2 s.
- **Race sims** (32 races per configuration, the average bot; M6 → now):

  | | Wreckin' oval | Wreckin' figure-8 | Stock Car oval | Stock Car figure-8 |
  |---|---|---|---|---|
  | Winner's time | 250 → 212 s | 247 → 200 s | 232 → 182 s | 224 → 191 s |
  | Spins in the field per lap | 3.6 → 5.1 | 7.3 → 9.1 | – | – |
  | Wrecks per race | 1.2 → 4.8 | 4.6 → 6.7 | 1.4 → 2.7 | 2.0 → 4.0 |
  | Bot wrecked before half distance (≤ 10%) | 9% → 3% | 12% → 9% | 3% → 0% | 9% → 6% |
  | Bot finished / mean place | 81% / 16.8 → 72% / 13.4 | 44% / 15.6 → 66% / 11.6 | 72% / 17.8 → 91% / 14.5 | 62% / 16.5 → 81% / 12.3 |
  | Pit stops per race | 3.1 → 10.5 | 2.3 → 9.9 | 3.8 → 14.7 | 1.4 → 14.3 |

  - All the Wreckin' targets are met. The wrecks are now in line with DD2's 4.75 a race, and early wrecks stay rare, as in DD2.
  - **Stock Car:** the spread from 1st to 10th on lap 2 is wider, 9.0 s and 16.7 s (M6: 6.6 s and 8.9 s; the target ≤ 6 s was missed in M6 too). The close battles in the last two laps: 47% on the oval (target 50%) and 62% on the figure-8.
  - **Racer against hunter bot** (equal skill, 32 seed pairs per layout): 5% apart over both layouts (target ±15%). On the oval the hunter is 16% ahead, on the figure-8 the racer is 22% ahead; two batches of the same layout differ by that much, so it's within the noise.
- **The derby** (80 derbies against M6): every target still passes. The cars grip more above 50 km/h, so the bot survives longer (113 s instead of 90 s) and hits a little less (19 hits a minute instead of 24). One derby in 80 ran into the 10-minute limit: the last two cars circled each other slowly for 7 minutes. That's an old rare case (the M4 strategy runs had it too); the "rocking in place" rule only catches cars that stay within 1.5 m. I've flagged a separate fix.
- **DD Code tactics:** T1–T5 succeed in 10, 9, 10, 10 and 10 of 10 trials.
- **Time Trial medals,** measured again (6 seeds × 5 laps): gold is reachable by the skilled bot and not by the average bot for 35 of the 36 cars and layouts (the Brougham on the oval is flat out for both). The laps are about 28% faster: oval gold 20.1–21.3 s (was 27.6–29.1 s), figure-8 23.7–24.6 s (was 33.2–34.7 s). **Your earlier Time Trial records were set with the old handling and are slower than the new bronze,** so the medals start over.
- **Not done (for you to decide):**
  - **The DD slide:** DD2's cars are turned 15–40° into the corners; ours show about 10° (a 4.5° slide plus the camera's lag). The look could be added on top of the grip; say if you want it.
  - **Race length:** the laps are shorter now (oval 21–24 s, figure-8 29–33 s), so the winner needs about 3.5 minutes. DD2's races take 5–8 minutes. Raising the laps from 8 to 10 (oval) and from 6 to 8 (figure-8) would bring the winner to about 4.5 minutes.
- **For your playtest:**
  1. Steering: does it feel like DD2 now? Hold a key through a fast corner, let go, try a brake dab.
  2. The opponents: are they quick off the line now, and fast enough in Division 4? If you have a higher division unlocked, try that too.
  3. The derby: does the Bowl still feel right with the extra grip above 50 km/h?
  4. Race length: keep 8 and 6 laps, or go to 10 and 8?

## M8 Polish — ready for your review (2026‑10‑19)
- **Play it:** double-click **Play Destruction Racing.cmd**, as in M7. Your settings and championships carry over. The new options are on Options → Graphics (FILM LOOK, MOTION BLUR, the longer anti-aliasing list).
- **Replays and highlights** (plan §15.5):
  - A replay now shows the damage as it happened: every dent, scrape, broken window and lamp at its moment, and the parts that came off fly again along their own paths. A self-test rebuilds every car's damage from the replay and compares it with the car as the event left it: **20/20 cars bit for bit**, in a derby and in a race.
  - **HIGHLIGHTS** on the results screen (H): the three biggest moments of the event (big hits, wrecks, rollovers, flying parts), each at half speed from the TV cameras; → skips to the next.
  - A free camera in the replay (C): the arrows turn it, +/− zoom.
- **The final mix** (plan §12), measured with a loudness meter on every bus (`--mixlog`, integrated level about LUFS):
  - The announcer about −17 on top; the engines −22 to −21; the crowd −26 to −25; the music −25 at the start, rising to −19 at the finale. The master is about −18, and only the derby's biggest crashes reach the limiter.
  - The engines dip 4 dB and the music 7 dB under the announcer.
  - **The music:** the eight tracks measured −16.3 to −19.5 dB, so some were 3 dB louder than others. Each is now matched to −18. Your own music files are assumed to be louder (a modern master) and turned down to match.
- **The full announcer set** (Kokoro TTS, as before): 58 new lines, 509 in all.
  - **The Duel:** the intro, lulls, your win or loss. **Total Destruction:** the intro, the minute calls, "still alive", the end and a new record (no more "hunted" calls, since everyone hunts you).
  - **The championship:** round and finale intros, promotion, the title and a missed promotion (in the season review).
  - **Time Trial:** the intro, a new best lap, each medal the first time you reach it, an invalid lap.
- **The look** (plan §8):
  - **A colour grade per light:** golden hour a little warmer with blue in the shadows; night with lifted blue-green shadows and a touch more saturation; the afternoon left as it was.
  - **FILM LOOK** (on by default): a soft vignette and fine grain in the mid-tones. **MOTION BLUR** (40% by default, 0–100%): blur along each pixel's own motion, so the speed reads; none on camera cuts.
  - **Depth of field** in the TV and replay cameras (the stands soft behind a sharp car), the showroom and the venue preview behind the menus. Never while you drive.
  - **Huge hits:** a short zoom-blur pulse from the impact point, on top of the camera punch and the flash.
  - **Rust:** an old livery (wear) now rusts along the sills and blisters on the panels; decals cover it.
  - **Anti-aliasing:** OFF, FXAA, MSAA ×2, MSAA ×4 + SMAA, TAA, or AMD FSR 2 (upscaling with its own anti-aliasing).
- **The fidelity checklist** (`docs/fidelity_checklist.md`, plan §17.2): every DD rule, its status and its evidence. M8 built the open items it found:
  - an optional **TIME LIMIT** for a Single Event derby (none, 3, 5 or 10 min: then points, then the car in the best shape);
  - the high **ARENA** camera (the camera key cycles to it: 13.5 m back and 9 m up, for the wider view DD's camera lacked);
  - **world popups** over the car you spun or wrecked;
  - **medal colours:** Time Trial medals open bronze, silver and gold paint in the garage;
  - pileup and rollover tests for the spin scoring (4 new unit tests, 102 in all).
- **The Purist preset** (Options → Gameplay → PURIST): all four gameplay assists off in one go (counter-steer, traction control, the back-marker catch-up and the slow motion). The AI keep their own assists.
- **Performance** (plan §16; the full checklist with every number is in `docs/m8_checklists.md`). At 4K Ultra on your PC:

  | Scene | Mean fps per 10 s window |
  |---|---|
  | Derby, afternoon | 140–263 |
  | Derby, night | 144–203 (seed 3); 103–202 (seed 4, one window of 19 below 120) |
  | Race, afternoon / night | 262–311 / 146–227 |
  | 20-car pileup | 193–206 |

  - **Garbage collection:** the hot code made a little .NET garbage with every engine call (every ray, string name and dictionary), every physics tick, and now and then a full garbage collection froze the game for 50–130 ms. That per-tick garbage is gone; in the pileup the pauses dropped from 103–141 ms to 5 ms.
  - **Draw calls:** only the body, panels, bumpers, tyres and rims cast shadows, and the small parts are hidden beyond 70 m (further through a long lens). The night race went from 95–156 to 133–198 fps.
  - **The smoke** was the most expensive thing late in a night derby (GPU 9–14 ms, 72–110 fps). It is now lit per corner instead of per pixel and skips its empty fringe: it costs 2.0 ms instead of 4.8 ms in the heaviest stretch, and looks the same.
  - **Wrecks** burn for 9 s (25 s if the engine bay was already gone), then smoulder, and the plume thins over 20 s. Before, every wreck with its front below 8% burned for good, and late in a derby ten fires and their lights cost about 3 ms. Fire lights also fade out beyond 30 m.
  - **Budgets:** physics 0.6–0.8 ms a tick, AI ≤ 0.17 ms, deformation ≤ 0.22 ms, at most about 2,900 particles, VRAM 2.0–2.7 GB: all inside.
- **Beauty** (plan §17.3): passes (`docs/m8_checklists.md`). Fixed on the way:
  - The low TV cameras stood 12 cm behind the catch fence, so the long lens filled the frame with giant wire diamonds. They now stand on the wall cap, inside the fence.
  - The leader's beacon filled a third of the frame when it passed close to a camera; its size is now capped up close.
  - A wrecked player in the Bowl read "WRECKED – RACE OVER"; now "WRECKED – YOU'RE OUT" (a race: "WRECKED – DNF").
- **Not done (deviations):**
  - **Car triangles** (✱ A30): 283k–400k a car at full detail against the plan's 60k. The tyres have real tread (14k each), the rims are 12k, the bumpers 18k; Godot's generated LODs bring them down to 12k–17k. The 4K target holds, so I left the cars as you approved them. Lighter tyres and rims would be the fix if a mid-range PC needs it.
  - **Shadowed lights** (✱ A31): 10 floodlights at night against the plan's 6. That's the M6 night look, and it stays above 120 fps.
  - **The spin cap after hits** (✱ A32): the pileup test found spins of 29 rad/s; the car's 14 rad/s cap now also holds after a hit.
  - **Mid-range and minimum PCs** (RTX 3060, GTX 1660): can't be measured on this PC. The 4K Ultra headroom suggests they are within reach; this needs testers (M9).
  - **Sticky Keys** opt-in: still not built. It changes a Windows setting; say if you want it.
  - **Anti-farming** (plan §5 changed, for your review): wrecking a stopped car that is still running scores the wreck, as finishing off a stuck car did in DD. Spinning a stopped car scores nothing, and hits on wrecks still score nothing.
- **Acceptance** (plan §18): "Performance and beauty checklists pass". The performance checklist passes on this PC, with the two budget deviations above and one night-derby window at 103 fps. The beauty checklist passes on stills; the motion checks (LOD popping, fog ghosting) are for your eyes.
- **For your playtest:**
  1. The look: do you like the grade, the grain and vignette (FILM LOOK) and the motion blur at 40%? Try them off and on in Options → Graphics.
  2. Watch a replay, and press H at the results for the highlights. Do the three moments it picks feel like the best ones?
  3. The mix: is the music loud enough against the engines and the crowd, and the announcer clear on top?
  4. Try a Duel, a Total Destruction and a Time Trial for the new announcer lines.
  5. Do the new ARENA camera and the world popups work for you?
  6. Anti-farming: is it right that finishing off a stopped car scores the wreck?
  7. Still open from M7: the upper-division difficulty and the Stock Car difficulty.

## M7 Championship and front end — approved (2026‑10‑18: "It is very good")
- **Play it:** double-click **Play Destruction Racing.cmd**. It replaces Play Derby.cmd and Play Racing.cmd, and the game now starts at its own front end. The first launch asks for your key layout (Classic DD PC, Arrows or WASD); after that, the title and the main menu.
  - Everything works from the keyboard: ↑/↓ choose, ←/→ change, Enter go, Esc back. The Del key deletes a championship (press it twice).
  - Your settings, championships and progress are saved in `%APPDATA%\Godot\app_userdata\Destruction Racing\` (`settings.json`, `career\slot1..3.json`, `profile.json`). A championship saves itself after every event.
- **The championship** (plan §4.5, `src/Rules/Career.cs`, unit-tested):
  - Wreckin' (four races, then the Bowl finale) or Stock Car (four races), in one of three save slots, from Division 4. 20 drivers in four divisions of five; every event has all 20 cars, and you are ranked in your division. The roofs show the divisions (yellow, blue, red, black).
  - Each division has its own calendar: the layout, direction and light of each round. Division 4 runs the oval and the figure-8 forward in daylight; Division 1 runs mostly at night, half of it in reverse.
  - The top of your division goes up; otherwise you repeat the season. The top AI of each division goes up and the last AI of the one above comes down, so your rivals change. You are never relegated.
  - **Promotion opens** that division's layouts, directions and lights in Single Event, Practice and Time Trial (Options → Gameplay can open everything). **Winning Division 1** opens the face-off start in the Bowl and eight metallic champion colours.
  - **The hub:** the season's calendar, your division's table with roof colours (←/→ shows the other divisions), the stakes ("YOU LEAD BY 10 · ONE ROUND TO GO"), your car, and GARAGE to change car, colours and number between rounds.
  - **During an event:** a live table of your division, with the points as they stand.
  - **After it:** CONTINUE takes you to the hub, where the table counts the points in round by round. After the last round, the season review: the verdict (promoted, champion, or try again), what it opened, and who went up and down.
- **The front end** (`src/Menus`):
  - **Title and showroom:** your car on a turntable in a lit studio.
  - **Car select:** the nine cars with stat bars and the DERBY / WRECKIN' / PACE verdicts. **Colours:** 16 colours (plus the champion set), the livery style and your number (the drivers' own numbers are taken).
  - **Single Event:** Destruction Derby, Wreckin' Racing, Stock Car Racing, **Duel** (you and one rival you pick, in the Bowl) and **Total Destruction** (DD1: every car hunts you; your score is the seconds you survive plus your points, and the best is kept). Pick the venue, layout, direction, light, the number of AI cars and the division they drive like. A preview of the venue shows behind the menu. Combinations you have not opened yet say what opens them.
  - **Practice:** the Bowl or the Speedway, with 0 to 19 AI cars. In the Bowl they can stand still as parked targets, to try the damage; on the Speedway you set the laps. **Time Trial:** as in M6, from the menu.
  - **Options:** Display (window, borderless or exclusive full screen, vsync, frame cap, render scale with FSR), Graphics (a preset and its parts: shadows, anti-aliasing, ambient occlusion, glow, the night haze, detail), Camera (default view, field of view, shake, slow motion), Sound (master, music, effects, engines, crowd, announcer, how often the announcer speaks, radio taunts), Controls (the preset, rebinding, the keyboard test, counter-steer and traction assists), Gameplay (Easy / Normal / Hard, unlock everything).
  - **Rebinding:** pick an action and press the new key; a key already in use swaps over. F1, F3, F5 and Enter are reserved. The screen warns about Shift (Windows' Sticky Keys prompt after five presses).
  - **The keyboard test** (the ghosting test, plan §13): hold the keys you drive with. It draws the keyboard, counts how many keys it sees at once, and checks four combinations of your own keys that a race needs at once (such as gas + steer left + fast steer + turn-around). Some keyboards cannot report three or four keys at once; this shows whether yours can.
  - **Credits**, read from CREDITS.md.
  - **Pause** (the pause key): resume, restart, back to the championship or the menu, quit. **Results:** continue, race again, watch the replay, menu, quit.
- **Division difficulty** (plan §10.5, ✱ A25):
  - The racing AI drives faster in the higher divisions (×0.97 of its pace in Division 4 up to ×1.015 in Division 1, as in M6) and attacks more often (×0.8 in Division 4 up to ×1, the M6 level, in Division 1).
  - Easy and Hard shift the pace by 2% and the aggression by 0.2 each way, and set how many derby AI may go for you at once (the attention budget, 2/3/4 by division).
  - The drivers start in the divisions their sim points put them in: in Wreckin' the bruisers in heavy cars are in Division 1 and the rookies in Division 4; in Stock Car the racers in the Volants, Adler and Hikaris are in Division 1.
- **Championship sims** (`bash tools/dev/championship.sh`): 400 events over every division's calendar, with the real field and the PlayerProxy "average fan" (in the Nordvik for Wreckin', the Ryujin for Stock Car). `championship_report.py` then plays 4,000 careers from them, with the tables, promotion and swaps as in the game.

  | Target (plan §17.4) | Division 4 | Division 3 | Division 2 | Division 1 (the title) |
  |---|---|---|---|---|
  | Wreckin': promotion chance per season | 47% | 19% | 15% | 12% |
  | Wreckin': median attempts (target 1–2) | **2 ✅** | **4 ❌** | **6 ❌** | **more than 12 ❌** |
  | Wreckin': seasons decided before the Bowl finale (target ≤ 30%) | 27% ✅ | 15% ✅ | 13% ✅ | 11% ✅ |
  | Stock Car: median attempts (target 1–2) | **never ❌** | – | – | – |

  - **The Bowl finale keeps seasons alive: met.** 16% of seasons are decided before it, and the bot is still in the hunt before the finale in 35–78% of them.
  - **Wreckin' promotion: met in Division 4 only.**
    - The average fan scores what the average AI driver scores, in every division (the AI's points per event are 0.94–1.13 of the bot's).
    - A season is five events among five drivers, and one event swings a lot, so even drivers each top the table about one season in five. Division 4 has the rookies and the calmest AI, and the bot goes up in 47% of seasons there. Higher up its rivals are as good as it is: 19%, 15%, and 12% for the title.
    - 17% of average-fan careers win the title within 12 seasons (median 10 seasons). A player better than the bot climbs faster.
  - **What I tried for Wreckin'** (each on 24–48 paired races or the full sims):
    - Sorting the starting divisions by strength: kept; it adds only about 2 points of promotion chance, because the drivers are close (45–62 points an event).
    - The AI's pace: it hardly changes Wreckin' points, which come from spins.
    - AI that never go for the player: no change. The bot's wrecks (about a third of Division 4 races) come from traffic and walls, not from being hunted.
    - The AI's aggression: kept as the division ramp above. At ×0.8 in Division 4 the bot's season points went from 250 to 305 against rookies on 193–234.
    - Each event pool holds 10–20 races, so a pool's mean is only good to about ±10%. That's too coarse to tune Divisions 3–1 any finer.
    - Scaling the AI's points offline shows what the target needs: the AI about 20% below the average fan in every division. In the game that means calmer AI everywhere, with fewer spins and less chaos than the M6 races you approved. That's your call (question 2 below).
  - **Stock Car: the bot can't measure it.**
    - Every bot that drives through the keyboard path finishes about 18th of 20: the average fan in the Ryujin or in the Kitsune, and the skilled bot too. In M6 it was 17.8.
    - It rides a metre off the outside wall at 120–140 km/h, and its small steering errors put it into the wall (hits of up to 800 kJ). Then it pits or retires.
    - Neither an AI 12% slower nor an AI that never bumps changed that.
    - So Division 4 Stock Car is the M6 field you played, with less bumping (×0.8 aggression), and your playtest has to judge it (question 3).
- **Load times** (plan §17.4: load ≤ 5 s, restart ≤ 3 s; windowed, RTX 5090): **met.**
  - The title is on screen 1.5–1.7 s after the game starts. The launcher builds first, which takes a few seconds more.
  - From START in the menu, a race is on screen in 1.4 s (the green light follows the 3 s countdown), and a derby in 0.8 s.
  - A restart takes 1.4 s for a race and 0.5 s for a derby.
  - The menus run at 740–900 fps. The venue preview keeps the Speedway cached (VRAM 600 → 920 MB, no more on later visits).
- **Not done (deviations):**
  - **Sticky Keys** (plan §14): the opt-in "suppress the Sticky Keys shortcut while playing" is not built. Testing it would mean changing your Windows accessibility settings, which I don't do on your machine. No preset uses Shift, and the rebinding screen warns if you bind it. It can come in M8 if you want it; you would test it.
  - The **Infield road course** (from M6) is still not built.
  - **Promotion attempts** (plan §17.4): met in Division 4 only, and Stock Car can't be measured with the bot (above).
- **Acceptance** (plan §18): "A full championship playable from start to finish". Both championships play from the first round through the season review and on into the next season. Checked in windowed runs:
  - the hub, and events started from it;
  - the results going back to the hub and the tally;
  - a promotion in the season review;
  - a slot that saves after each event and loads again.

  No whole career to the title was played by hand; the sims play thousands.
- **For your playtest:**
  1. Start a Wreckin' championship and play a season through to the Bowl. Is the hub clear (the table, the stakes, the calendar)? Does the tally after each round work for you?
  2. Is Division 4 the right difficulty for you at Normal? Did you go up first time? Higher up, an average player needs four to six seasons per division (the bot's numbers). Is that a fair climb, or should the AI in Divisions 3–1 be calmer, so you go up in one or two (with fewer spins in the races)? Easy already makes them calmer.
  3. Try a Stock Car championship too. Where do you finish in Division 4, and can you top the five yellow roofs? The bot can't tell me: it finishes at the back.
  4. Single Event: try a Duel and a Total Destruction. Should Total Destruction be harder or easier?
  5. Options: does the keyboard test show all your keys? Do the graphics and sound settings do what you expect?
  6. Anything in the menus that is slow, unclear or missing?
- **Your verdict** (2026‑10‑18): you played it and approved it. The upper-division difficulty, the Stock Car difficulty and the Sticky Keys opt-in stay open for M8.

## M6 Racing — approved (2026‑10‑18: "It's very cool!")
- **Play it:** double-click **Play Racing.cmd** (it replaces Play Wreckin.cmd). The set-up screen:
  - **EVENT:** Wreckin' Racing, Stock Car Racing or Time Trial.
  - **LAYOUT and DIRECTION:** the oval or the figure-8, forward or reverse.
  - **LIGHT:** afternoon, golden hour or night (floodlights, headlights on).
  - **CAR, COLOUR, STYLE, DIVISION:** as in the derby; the car shows its WRECKIN' and PACE verdicts.
  - After a race: **V** watches the replay, **R** races again, **M** goes back to the menu, **Q** quits.
- **Crossroads Speedway** (plan §7.3), generated by `tools/tracksmith/crossroads.py` and dressed by `SpeedwayLookBuilder.gd`:
  - One venue, two layouts on shared banked turn ends (12°): the **oval** (895 m, 8 laps) and the **figure-8** (984 m, 6 laps) with the blind crossover in the infield. Both run in either direction. The roads are 13 m wide, three cars abreast.
  - Each layout has its own pit lane with four boxes. Where a layout's road ends inside the other's, red-and-white barrier blocks close it off.
  - **The look:** splat-blended ground (asphalt, concrete, dirt, grass) with a worn racing groove and oil drops; concrete walls with a catch fence; the main stand (132 m, 14 rows) and the back stand with the crowd; pit garages; start gantries with five start lights; sponsor billboards that make the crossover blind; 10 floodlight towers; trailers, a hospitality tent and flags in the infield; hills and trees around it.
  - **Three lights,** each baked: afternoon (the Golden Gate Hills sky), golden hour and night. At night the floodlights now light the whole lap: I placed and aimed the towers from a coverage search over both racing lines (the darkest stretch of the lap went from 0.04 to 0.15), and the stand roofs no longer throw a band of shadow across the straights.
  - **Crash cushions:** water barrels on the pit-wall noses that face traffic, and soft island tips at the crossover. A hit there does 30% of the damage ✱.
- **Racing AI** (`RaceBrain`, plan §10.3), now with the full DD Code (plan §4.7):
  - **On offence:** T1 brake-checks on a tailgater (aggressive drivers, on the straights), T2 turn-ins on the rear corner of a car overtaking them, T3 taps on a car braking for a bend, T4 squeezes of a car beside them against the wall or a wreck (not at the open crossover), and the leader-killer: cars up to 40 m behind the ×2 leader, or being lapped by it, go for its rear corner.
  - **Falling for it:** low-skill drivers tailgate in the draft, right on the bumper, and see the car ahead brake up to a quarter of a second late, so a brake-check catches them. Naive drivers brake predictably into the bends (T3 bait).
  - **On defence:** skilled drivers dodge a car turning into their side (they jump a lane or put the power down) and block the inside into a bend.
  - **Slipstream:** −35% drag up to 25 m behind another car.
  - Recovery: a car stuck off the road for 10 s is put back on the track; a rolled car is righted after 3 s, on the road; a car thrown over a wall into an infield island is put back after 3 s.
- **Wreckin' Racing:** as M1c, on the new venue, with one pit stop and the slipstream. Finished cars do a cool-down lap as see-through ghosts, so they can't block the cars still racing.
- **Stock Car Racing** (plan §4.3): points for the finishing order only (50, 40, 35 … 0), contact scores nothing, unlimited pit stops. The AI drives 3% faster than in Wreckin' (DD1). The pack races clean for two laps; from lap 3 the bruisers and psychos bump (half and 60% of their Wreckin' aggression), grudge-holders a quarter, the rest a tenth. Everyone but the psychos yields at the crossover.
- **Pit stops and the repair mini-game** (plan §6.0):
  - Drive into the lane; the crew catches you at your box. Choose a zone with ←/→ and mash Enter or Space: each press repairs 3.5 health on that zone (and half on its wheels), at most 15 presses a second. You have 5 s; ↑ leaves early. The camera orbits to the zone being repaired.
  - Wreckin' allows one stop (the HUD says when you have none left), Stock Car any number.
  - The AI and the bot decide on damage and laps left and repair at 5–9 presses a second by skill.
  - Cars in the lane are ghosts to the traffic on the track.
  - **Fixed in the sims:** a car that had asked to pit but arrived on the track side of the pit wall was steered into the wall's nose and stuck there (the cause of most early player wrecks on the figure-8). The approach now needs the car really in the lane, gives up if it missed the mouth, and backs out of anything it gets stuck on.
- **Time Trial** (plan §4.4): alone on the track, flying laps. A stopwatch, three sector splits against your best, last and best lap, the ghost of your best lap (G hides it), medals and a top-10 board per layout, direction and car. **R restarts at once.**
  - Medal targets per car from the bot: gold = the skilled bot's best + 0.4%, silver = the average bot's best + 1%, bronze = + 5%. Gold is reachable by the skilled bot and not by the average one for all nine cars on every layout and direction, except the Homestead on the oval forward (by 0.01 s).
- **Rollovers:** a car on its roof or side for 2 s gets the ROLLOVER! popup and is put back on its wheels at 3 s, on the track, facing the right way. **Wrecks** stay on the track as obstacles; the AI steers round them or shoves them aside.
- **Sound:** the derby's sound director now runs every event (`EventAudio`). The announcer has race commentary (`RaceCommentary`), with 106 new lines: layout and event intros, the start, the crossover, the leader spun, pit stops, the final lap, a close finish, overtakes, your win, podium or finish, and the winner's number. The crowd reacts to hits, spins, wrecks and the finish.
- **Replays:** V at the results, as in the derby, with TV cameras placed round the Speedway (on the towers, the stand roofs and low on the walls every 55 m). The replay starts 8 s before your wreck, or 15 s before your finish.
- **Checks** (`bash tools/dev/race_targets.sh 8 4`: 32 races per configuration with the PlayerProxy "average fan" in the Kitsune, Division 4):

  | Target (plan §17.4) | Wreckin' oval | Wreckin' figure-8 | Stock Car oval | Stock Car figure-8 |
  |---|---|---|---|---|
  | Spins in the field per lap ≥ 3 | 3.6 ✅ | 7.3 ✅ | – | – |
  | Crossover incidents per race ≥ 2 | – | 23 ✅ | – | – |
  | Leader spun per race ≥ 2 | 2.0 ✅ | 2.0 ✅ | – | – |
  | Spread 1st–10th on lap 2 ≤ 6 s | – | – | **6.6 s ❌** | **8.9 s ❌** |
  | Close battle with the player, last 2 laps ≥ 50% | – | – | **34% ❌** | 50% ✅ |
  | DNFs per race 1–4 | – | – | 1.0 ✅ | 1.5 ✅ |
  | Player position changes ≥ 6 | 25 ✅ | 34 ✅ | 22 ✅ | 27 ✅ |
  | Cars within 3 s of the player ≥ 60% | 89% ✅ | 92% ✅ | 86% ✅ | 84% ✅ |
  | Player wrecked before half distance ≤ 10% (Wreckin') | 9% ✅ | **12% ❌** | (3%) | (9%) |
  | Player stuck or recovering ≤ 5% | 1.4% ✅ | 1.6% ✅ | 1.2% ✅ | 1.2% ✅ |
  | Races ended normally | 100% ✅ | 100% ✅ | 100% ✅ | 100% ✅ |

  - **25 of the 29 met.** The misses, for your playtest:
    - *Stock Car spread on lap 2:* 6.6 s on the oval and 8.9 s on the figure-8 (target ≤ 6 s). The pack races clean for two laps (no bumping before lap 3), but on the figure-8 a single spin in a loop stacks up the pack behind it. The oval was 5.5 s in an earlier run and 6.3–6.6 s in the last three: on the line.
    - *Stock Car late battles on the oval:* 34% (31–44% across runs). The bot finishes around 18th there: it takes enough damage to pit about once (0.8 stops a race, half a lap each), and at the back there is less to fight.
    - *Wreckin' figure-8, player wrecked before half distance:* 12%, 4 of 32 races (6% and 9% in the two runs before; the 95% interval at 32 races is about ±11%).
    - *The leader spun on the oval* met the target this time (2.0), but it was 1.0 in the earlier runs: borderline. With no crossover the leader is rarely near anyone; hunters can't close a 40 m gap at the same top speed.
  - **DD Code in the field, per race:** brake-checks 1–4 (30–40% catch the tailgater), squeezes 2–11, tailgating stints 11–29, dodges 15–20, blocks 9–16. More in Wreckin' than in Stock Car.
  - **What the sims found and fixed along the way:**
    - The pit approach that pinned cars against the pit-wall nose (above).
    - Barrels that stood on the road: the walls are shared by both layouts, so the nose of one layout's pit wall, and the crossover tips, are the very edge of the other layout's road. With barrel colliders there, the Stock Car spread went from 6.5 to 11–13 s. Barrels now only stand where none of them touches this layout's road or pit lane.
    - A flipped car being shoved along on its roof was never righted (the rule waited for it to stop). It now counts once the car has stopped turning over.
    - Cars thrown into an infield island: at the crossover a car hit side-on rides up on the car that hit it, and at about 1.5 m up it can clear the island's 1.2 m tyre wall. Inside, it drove on along the wall, still making progress, until it was trapped at the island's far end (twice in 800 car-laps on the figure-8). A car inside an island is now put back on the track after 3 s.
    - The Volant's front drive gave it more grip on both axles (front ×1.03, rear ×1.04), against the code's own note of less front grip; with it the Volant was at +58% in Stock Car. Details under Balance.
  - **Time Trial medals** (`TimeTrialSim`, 6 seeds × 5 laps): gold reachable by the skilled bot and not by the average bot for 9/9 cars on the figure-8 both ways and the oval reverse, and 8/9 on the oval forward (the Homestead: the average bot's best is 0.01 s under gold; the heavy wagon is flat out for both bots). The Volant's targets were measured again after its grip change: its oval gold is now 27.76 s (was 27.51 s), behind the Adler's 27.62 s.
  - **Endurance** (`bash tools/dev/endurance.sh`, plan §17.1: "the AI completes 100 laps on every layout in both directions without getting stuck"): 8 cars, no damage, one 100-lap race per layout and direction.
    - **Oval, both directions: met.** Every car did its 100 laps; nobody was put back on the track, and the longest stop was 9.2 s.
    - **Figure-8: not met, strictly.**
      - 15 of the 16 cars did their 100 laps. The 16th, a Homestead in reverse, was pinned against the outer wall of the left turn on lap 37 and wrecked by the DD rule after 10 s.
      - Cars were put back on the track 10 times in 1,600 car-laps: 9 times after being thrown over a crossover face into an infield island (the new 3 s rule), once after 10 s stuck off the road in a crossover tangle.
      - The longest stops were 10–23 s, most likely in tangles: with no damage, nothing breaks a tangle up the way wrecks do in a race.
  - **Load times** (plan §17.4: restart ≤ 3 s, load ≤ 5 s): the engine is up 1.4 s after launch and the race is on screen 3.4 s later (4.75 s from the double-click; 5.7 s on a cold first run). **R** to restart: the race is back on screen in 1.85 s; the green light follows the 3 s countdown. Time Trial restarts in place, at once.
  - **Performance** (RTX 5090, 1920×1080, a 20-car night race on the oval driven by the bot, 95 s): 130–202 fps on average, 1% lows of 105–171 fps. On the grid in the first 5 s: 117 fps, with 1% lows of 80 fps while the shaders compile. Up to 9.7k draw calls on the grid (ten shadowed floodlights and the headlights); 0.76 GB VRAM. The plan's 4K target for high-end hardware is not measured yet.
  - **The derby still meets every plan §17.4 target** (80 derbies):
    - First contact 1.7 s; event 181 s; survival 90 s; 23.7 player hits a minute.
    - Wrecked within 30 s / 60 s: 1.2% / 12.5%. Points from deliberate play: 100% (median).
  - **DD Code T1–T5** (scripted tactics):
    - 10/10 each with the Kitsune; 10/10 with the Brougham, except T4 at 9/10.
    - With two Volants, T2 is 0/10 and T5 3/10. In T2 the cars never touch ("hits 0"): the scripted turn-in (full lock and full throttle for 0.9 s) doesn't reach the overtaker 1.3 m away. My guess is front-drive understeer under full throttle; worth trying T2 in a Volant in your playtest.
  - **Pileup test:** pass. 2.29 ms per physics tick while 16 sims ran beside it; 0.93 ms alone earlier in M6, against the 3.5 ms budget.
  - **86 unit tests** pass.
- **Balance** (plan §9.3, the two M5 carry-overs): `bash tools/dev/balance.sh 120 16`: 120 derbies, 120 Wreckin' races and 120 real Stock Car races, every driver (and the bot) in a random car. Mean event points against the mean of the nine cars; for Stock Car also against the six track cars alone ("peers"), and how often a car was spun after a hit:

  | Car | Derby | Wreckin' | Stock Car | Stock Car vs peers | Spun per Stock Car race | Car select says |
  |---|---|---|---|---|---|---|
  | Brougham | **+8%** | −0% | −40% | | 2.6 | derby STRONG · Wreckin' GOOD · pace WEAK |
  | Homestead | **+14%** | +8% | −36% | | 2.6 | BEST · STRONG · WEAK |
  | Interceptor | **+1%** | **−3%** | **−13%** | −23% | 2.0 | GOOD · GOOD · WEAK |
  | Nordvik | **+2%** | +3% | −10% | | 2.1 | GOOD · GOOD · FAIR |
  | Adler | −8% | **−7%** | **+17%** | +2% | 1.8 | FAIR · FAIR · BEST |
  | Volant | +12% | **−0%** | **+38%** | +21% | 1.3 | BEST · GOOD · BEST |
  | Hikari | −10% | **+4%** | **+17%** | +2% | 1.8 | WEAK · GOOD · BEST |
  | Ryujin | **−5%** | **−3%** | **+9%** | −5% | 2.1 | FAIR · GOOD · STRONG |
  | Kitsune | −13% | **−1%** | **+17%** | +3% | 1.7 | WEAK · GOOD · BEST |

  Bold = the car's intended mode(s); the track cars are meant for both races. The 95% interval is about ±10% per car at 120 events.
  - **No car top 3 in every mode: met.** Derby: Homestead, Volant, Brougham. Wreckin': Homestead, Hikari, Nordvik. Stock Car: Volant, Kitsune, Hikari.
  - **±15% in the intended mode(s):**
    - Derby and Wreckin': met for every car.
    - Stock Car: not met. The Volant is at +38%. The Adler, Hikari and Kitsune (+17% each) are just out, but they are level with the other track cars (+2–3% against their peers).
    - Why the track cars sit high: Stock Car points are fixed per race (50, 40, 35 … 0), and the three derby cars finish at the back by design (−10% to −40%, PACE WEAK). So the six track cars sit about 13% above the nine-car mean even when they are equal. Against the cars meant for Stock Car, only the Volant (+21%) and the Interceptor (−23%) stand out.
  - **The Volant's pace** (the M5 question: +41% in the estimate):
    - In real Stock Car sims it was +58%. The sims found a bug: front drive gave it more grip on both axles (front ×1.03, rear ×1.04), against the code's own note of "a little less cornering grip". It is now front ×0.97, rear ×1.04, and the Volant is at +38%.
    - What is left: it is spun least by far (1.3 times a Stock Car race; the others 1.7–2.6) and finishes highest (mean place 7.8 against 9.2–9.6 for the next three).
    - Tried without a clear effect (64–120 races each; the interval is about ±15% at 64): less rear grip for front drive (down to ×0.85), less front grip after a hit, top speed 7 → 6, acceleration 8 → 7.
    - Also tried and dropped: scaling the AI's attack aim (1.7 m behind the target's centre, sized for a 4.6 m car) with the target's length. It did not move the Volant, and on 96 paired Wreckin' oval races it wrecked the average fan more often (14% before half distance against 8%).
    - In clean Time Trial laps the Volant is no longer quicker than the Adler. The AI drives every car to the same corner speeds, so its edge comes from traffic. My best guess is its size: it is the shortest car (3.72 m, half a metre shorter than the next) and the narrowest, so it may simply be hit less (the sims don't count hits per car yet).
    - Its ratings stay as you approved them. The car select says PACE BEST, and from these sims also derby BEST: it is hard to hit there too.
- **Not done (deviations):**
  - The **Infield road course** (plan §7.3, a *Should* "if on schedule"): not built. The venue has the room for it.
  - **Lap lengths:** the oval laps take about 29 s and the figure-8 about 35 s at Time Trial pace (31–37 s and 38–42 s in a race), against the plan's ≈ 35 s and ≈ 45 s. The figure-8 kept the M1c grey box's length, which you played; the oval shares its turns. Shorter laps mean more traffic, which the targets like; if they feel short, the tracksmith can stretch the straights.
  - Stock Car races are scored and played, but the **championship** comes with M7.
- **Acceptance** (plan §18): "Racing targets met; you playtest".
  - Race targets: 25 of 29 met (above). The misses are the Stock Car spread on both layouts, the Stock Car late battles on the oval, and the early-wreck rate on the figure-8 (near the line).
  - The derby still meets every target. Endurance is met on the oval. On the figure-8, one car of 16 was pinned and wrecked, and cars needed 10 recoveries after crashes.
  - Balance: no car is top 3 in every mode. Derby and Wreckin' are within ±15%. In Stock Car the Volant is at +38%.
- **Your verdict** (2026‑10‑18): you played it and approved it. The Volant (+38% in Stock Car) and the lap lengths stay as they are.

## M5 Car roster — approved (2026‑10‑15: "I follow your recommendation. Now continue")
- **Look at it yourself:**
  - **Play Derby.cmd**: the set-up screen now has all nine cars. CAR shows the five ratings as bars, the engine and the drive, and three verdicts from the balance sims (DERBY, WRECKIN' and PACE, each from WEAK to BEST). COLOUR is a palette grid of 16, STYLE the six livery styles. Your car carries #7 and the roof colour of your division.
  - **Car Studio.cmd**: Tab = next car, L = next livery (showroom, test liveries, then the drivers' signature liveries), B = derby damage, X = repair, arrows orbit, W/S zoom, 1/2/3 lighting.
  - **Bowl Look.cmd**: one driver per car plus three more, in their signature liveries.
  - **Damage Lab.cmd**: Tab = next model; all nine cars take the full damage chain.
- **The roster** (plan §9.1): nine fictional cars, three per region, all generated by `tools/carsmith` (eight are morphs of one parametric template: sedan, coupé, hatch, wagon; the Kitsune is the M2 hand-built body).

  | # | Car | Region | Body | Engine | Speed | Accel | Handling | Weight | Tough | Budget |
  |---|---|---|---|---|---|---|---|---|---|---|
  | 1 | Aldridge Brougham 400 | US | sedan, vinyl roof | V8 | 6 | 5 | 3 | 9 | 9 | 32 |
  | 2 | Aldridge Homestead | US | wagon, simulated wood, roof rack | V8 | 5 | 5 | 3 | 9 | 10 | 32 |
  | 3 | Columbia Interceptor GT | US | fastback, hood bulge, wing | V8 | 8 | 8 | 5 | 6 | 6 | 33 |
  | 5 | Nordvik Fjord DL | EU | sedan | I4 | 5 | 5 | 5 | 7 | 9 | 31 |
  | 6 | Adler Falke 2.7i | EU | coupé, quad round lamps | I6 | 8 | 7 | 8 | 5 | 6 | 34 |
  | 7 | Volant 105 GTi | EU | hot hatch, front drive | I4 | 7 | 8 | 8 | 3 | 4 | 30 |
  | 9 | Hikari Kaze GT | JP | liftback, pop-up lamps | I4 | 7 | 7 | 9 | 3 | 4 | 30 |
  | 11 | Ryujin Raiden GTS | JP | turbo sedan, boot wing | I6 turbo | 8 | 8 | 6 | 6 | 6 | 34 |
  | 12 | Kitsune Rotary GS | JP | coupé | rotary | 9 | 7 | 8 | 4 | 3 | 31 |

  Every budget is 32 ± 2 (plan §9.3).
- **Ratings become physics** (`CarInfo.Tuning`): mass 1,400 kg × (1 + MASS × (weight − 6.5)); springs, dampers, anti-roll bars, brakes, reverse and rolling resistance scale with the mass, so each car feels the same per kilogram; drive force and power scale with mass × acceleration; the drag sets the top speed (+3% per speed point); grip +2.5% and steering +2% per handling point; the Volant is front-wheel drive. Toughness divides the damage a car takes: 1 + TOUGH × (toughness − 6.5). MASS (0.045) and TOUGH (0.02) are the balance knobs (below).
  - Across the nine cars, 0–100 km/h takes 2.2–2.7 s and the speed after 14 s is 216–240 km/h.
- **Engine sounds:** six new synthesized engines (`tools/enginesynth`): the Homestead's and Interceptor's V8s, the Adler's and Ryujin's inline-sixes (the Ryujin with a turbo whistle), the Volant's and Hikari's fours.
- **Liveries** (plan §9.4):
  - Six styles: solid, twin stripes, two-tone, side band, cabin two-tone, side flash. Base colour, second colour, the race number on the doors and the roof, wear, dirt and metallic paint.
  - **Fictional sponsors** (`tools/liverysmith`, drawn by us in Barlow Condensed): Kraken Motor Oil, Volta Spark Plugs, Dustbowl Tyres, Hammerhead Auto Parts, Fizz-O Cola, Ironside Insurance, Rusty's Scrapyard and Big Jim's Towing, on the front fenders and the hood.
  - **Primer:** some drivers run a junkyard door or fender in grey primer.
  - **Roof colour** by division, as in M4.
  - The Homestead's simulated-wood sides are paint-shader panels, so they dent, scrape and burn like the rest.
- **19 named drivers** (plan §10.2), each with a nationality, car, number, signature livery and archetype:
  - **Bruisers:** Harlan "Buck" Doyle (USA, Homestead #3), Wade Kettering (USA, Brougham #11), Sven Lindqvist (Sweden, Nordvik #45), Shane O'Rourke (Ireland, Brougham #77).
  - **Racers:** Dieter Hollmann (Germany, Adler #9), Théo Lacombe (France, Volant #14), Kenji Moriyama (Japan, Hikari #86), Hiro Saeki (Japan, Kitsune #13).
  - **Vultures:** Anke Vos (Netherlands, Adler #31), Takeshi Okuda (Japan, Ryujin #32), Earl "Tank" Mabry (USA, Homestead #66).
  - **Grudge-holders:** Marta Kowalczyk (Poland, Nordvik #18), Ryo Hasegawa (Japan, Ryujin #19), Darnell Price (USA, Interceptor #24).
  - **Psychos:** Rosa Delgado (Mexico, Interceptor #22), Giulia Ferretti (Italy, Volant #27).
  - **Rookies:** Aiko Tanabe (Japan, Hikari #4), Callum Reid (Scotland, Kitsune #8), Bram De Smet (Belgium, Brougham #51).
  - The field of 19 is drawn in a seeded order; the HUD, the feed and the results use their names.
- **Radio taunts** (plan §10.2): a banner bottom left (the number on a badge in their paint, name, nation, the line) when a driver spins or wrecks you, or you spin or wreck them. Lines by archetype and moment; 30 s cooldown per driver and 8 s overall (2 s for wrecks). Frequency off / some / all (`--taunts=`; the M7 options menu will show it). Text only: voiced taunts are v1.1.
- **Parts from M3 that now come off:** hubcaps and wheel covers (on hits, a weak wheel, or with their wheel; they roll away) and the exhaust (a ruined rear or a hard hit near it).
- **Fixed: the cars had no glass since M4.** A mesh's surfaces share one block of per-instance shader values; the glass's "pane knocked out" flag and the rubber seal's "lamp lit" flag (on by default) used the same slot, so every pane was hidden. Each car-shader flag now has its own slot. The M4 build you approved was affected too.
- **Balance** (plan §9.3): `bash tools/dev/balance.sh 200 16`, 200 derbies and 204 Wreckin' races with every driver (and the bot) in a random car, so each car meets every archetype. Mean event points against the mean of the nine cars (95% interval about ±7–8%):

  | Car | Derby | Wreckin' | Pace (estimate) | Car select says |
  |---|---|---|---|---|
  | Brougham | **+11%** | +2% | −26% | derby BEST · Wreckin' GOOD · pace WEAK |
  | Homestead | **+14%** | +14% | −33% | BEST · BEST · WEAK |
  | Interceptor | **−9%** | **−7%** | −9% | FAIR · FAIR · FAIR |
  | Nordvik | **+9%** | +10% | −11% | STRONG · STRONG · WEAK |
  | Adler | −5% | **−11%** | +14% | FAIR · WEAK · BEST |
  | Volant | +8% | **−1%** | +41% | STRONG · GOOD · BEST |
  | Hikari | −7% | **−4%** | +12% | FAIR · GOOD · BEST |
  | Ryujin | **−4%** | **−2%** | +2% | GOOD · GOOD · GOOD |
  | Kitsune | −18% | **−2%** | +10% | WEAK · GOOD · BEST |

  Bold = the car's intended mode(s). **Pace** is an estimate: the Stock Car finishing points (50, 40, 35 … 0) for the race finishing order. Stock Car Racing itself comes with M6.
  - **±15% in the intended mode(s): met** for all nine.
  - **No car top 3 in every mode: not met in the two modes you can play now.** The Brougham, Homestead and Nordvik are the top 3 in both the derby and Wreckin'. Wreckin' points are about 90% spin points, and the slow, heavy cars spin the most others (the Homestead 56 spin points a race, the Adler 42). On pace they are last: mean place 11–13 against 8–10, and 34–48% finish against 51–62%. With Stock Car counted, no car is top 3 everywhere: the estimate puts those three at the bottom.
  - The car-select screen shows all three verdicts honestly: DERBY, WRECKIN' and PACE.
  - **What changed to get here:**
    - Toughness 0.07 → 0.02 per point. It decides the derby: at 0.07 the tough cars were at +18–27%.
    - Mass 0.055 → 0.045 per point: 1.32× from the lightest to the heaviest (plan: 1.0–1.5×).
    - Homestead weight 10 → 9 and acceleration 4 → 5.
    - Kitsune weight 3 → 4.
  - **What didn't help** (100 derbies each, paired seeds): mass 0.035 or 0.025 per point left the Homestead at +34–43% in the derby, so mass hardly matters there. Toughness 0.035 still left it at +24%.
  - **For M6:** the Volant leads the pace estimate by 41% (front drive, acceleration 8, handling 8). Check it with real Stock Car sims.
- **Checks with the new cars** (the drivers in their own cars, the bot in the Kitsune):
  - **Derby sims, 80 derbies: every plan §17.4 target is still met.**
    - First contact 1.7 s; longest idle stretch 6.6 s (median).
    - 23.5 player hits a minute; event 172 s; survival 96 s.
    - Points: 100% deliberate (75% from aimed hits only).
    - Wrecked within 30 s / 60 s: 0% / 12.5%. Ganged up on: 1%. Stuck: 3.1%. No time-outs.
    - The bot wins 8% (M4: 18%): its Kitsune is now the weakest derby car (−18%).
  - **Wreckin' sims, 40 races: all targets met.**
    - Spins per lap: 6.0–8.2 per batch of 10. Crossover incidents: 17.5–23. Position changes: 31–36.
    - The leader is spun twice a race (median over all 40; one batch of 10 had 1).
    - Cars within 3 s of the player for 89–92% of the race.
    - 0% wrecked before half distance, at most 0.5% stuck, no time-outs.
  - **DD Code T1–T5:** 10/10 each with the Kitsune. With the Brougham: 10/10, except T4 at 9/10.
  - **Pileup test:** pass (no launches, nothing through the floor, settled, no cars inside each other; 1.54 ms per physics tick).
  - **86 unit tests** pass.
  - **Looks:** the roster contact sheets (`out/roster/roster_front.png`, `roster_rear.png`, `roster_side.png`), the damage lab with the Volant, the Bowl with the signature liveries, the radio banner and the set-up screen.
- **Not done (deviations):**
  - The plan's livery compositing (a SubViewport baking one 2048² albedo per car) is done in the paint shader instead: the layers are computed per pixel, so there is nothing to bake and no texture memory per car.
  - Rust as pre-existing wear: age gives a faded, dull clear coat, dirt and primer panels, but rust only appears with damage so far. Rust patches from age could come with the M8 polish.
- **Acceptance** (plan §18): "Balance targets met; roster approved". You approved the roster on 2026‑10‑15 and chose to count Stock Car Racing as the third mode for "no car top 3 in every mode" (then no car is top 3 everywhere); the Wreckin' finish bonus stays 25/15/10.
- **Carried to M6:** confirm the top-3 rule with real Stock Car sims (derby, Wreckin' and Stock Car points), and check the Volant's lead on pace (+41% in the estimate).

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
