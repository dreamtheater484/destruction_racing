# Fidelity checklist (plan §17.2)

This list shows how closely the game follows the PC versions of Destruction Derby 1 and 2 (plan §3). It covers the **Standard** ruleset (DD2 PC values), the only one v1 ships; DD1 Classic follows in v1.1. It is the state at the end of M8 (19 Oct 2026). **✅** = implemented and tested (a unit test, a scenario test or a sim measured it). **☑️** = implemented, checked only by playing or by screenshots. **⏳** = not built yet, or deferred (to the milestone or version named). **✱** = the value is an assumption from plan §20.2 (its A-number follows). Test names are from `tests/Rules.Tests`; sim results are from `docs/PROGRESS.md`. Where a plan §17.4 target is still missed it is noted next to the rule; the full list is in PROGRESS.md.

## Rules

### §4.0 Ruleset (Standard = DD2 PC)

| Rule | Status | Evidence |
|---|---|---|
| v1 ships one ruleset, Standard (DD2 PC) | ✅ | `RulesConfig.Standard`; `ScoringTests` |
| 20 cars (you + 19 AI): 4 leagues × 5 drivers | ✅ | `Grid_sizes_match_PC_versions` |
| Spin 90° / 180° / 360° = 10 / 25 / 50 | ✅ | `Standard_spin_points_match_DD2_PC_manual` |
| Wreck = 25 | ✅ | `Wreck_points_per_ruleset` |
| All points ×2 if the car you damage is in 1st place | ✅ | `Points_double_when_the_damaged_car_leads` |
| Hitting an already-wrecked car scores 0 | ✅ | `Hits_on_wrecked_cars_score_nothing` |
| Points cap of 999 per event | ✅ | `Event_cap_is_applied` (the cap logic, tested at DD1's 99); 999 in `RulesConfig.Standard` |
| Wreckin' finishing bonus 25 / 15 / 10 | ✅ ✱ A1 | `Finish_tables` |
| Stock Car points 50, 40, 35 … 1, 0 | ✅ ✱ A2 | `Finish_tables` |
| The Bowl: spins and wrecks both score | ✅ | `Bowl_scores_spins_and_wrecks_in_both_rulesets` |
| The Bowl: the last car running wins; survivor bonus 25 | ✅ ✱ A15 | `Last_car_running_wins_and_order_is_reverse_elimination`; bonus in `DerbyMode.EndEvent`, run in the derby sims |
| Race end: continues until the player finishes or is wrecked | ✅ ✱ A11, A24 | as A24: ends when the player finishes, or (wrecked) when the winner takes the flag (`RaceMode`); M6 sims: 100% of races ended normally |
| Rollovers; a flipped car is righted after a time penalty | ☑️ ✱ A13 | `Car`: ROLLOVER! at 2 s, righted at 3 s; `--fliptest` (M6) |
| Wheel loss on heavy rear or wheel damage | ✅ | `Corner_hits_damage_the_nearest_wheel_and_heavy_rear_damage_loses_it` |
| Hood and boot lid fly off; fire | ☑️ | M3 Damage Lab: hood off below 8% front, boot lid below 10% rear, fire below 8% front |
| Pit stops: Wreckin' once, Stock Car as often as you like, 5 s per stop | ☑️ | `PitStops`, `RulesConfig.Standard`; the sims run stops but don't check the limits |
| Championship: 4 × 5, start in Div 4, Wreckin' 5 races ending in the Bowl, Stock Car 4, repeat without promotion, no relegation | ✅ | `CareerTests`, `ChampionshipTests` (rows under §4.5) |
| Roof colours: Div 4 yellow, Div 3 blue, Div 2 red, Div 1 black | ☑️ | `DerbyMode.RoofColor`; M4–M7 screenshots |
| DD1 Classic ruleset (16 cars, DD1 points, 99 cap, no pits, no flips, no wheel loss) | ⏳ v1.1 | data in `RulesConfig.DD1Classic` (its values are unit-tested); not selectable; the modes don't read its `RaceEnd` and `Rollovers` flags |

### §4.1 Destruction Derby (the Bowl)

| Rule | Status | Evidence |
|---|---|---|
| 20 cars; the last car running wins; the order is reverse elimination | ✅ | `DerbyStandingsTests` (4 tests) |
| Event points (spins + wrecks + survivor bonus) feed the championship | ✅ | `Session.ReportResults`; M7 championship sims (400 events) |
| Out when wrecked; wrecks stay as obstacles, smoke or burn, and can be pushed | ☑️ | M1b–M4 builds; a wreck burns for 9 s (25 s if its engine bay was already gone), then smoulders, its plume thinning over 20 s (M8) |
| No time limit by default | ✅ ✱ A8 | derby sims: every derby ends, no time-outs; a 600 s safety stop (`DerbyMode.MaxDuration`) also applies in play |
| Optional time limit in Single Event; then rank by points, then remaining health | ✅ | `A_time_limit_breaks_a_points_tie_on_remaining_health`; Single Event → TIME LIMIT: none, 3, 5 or 10 min (M8) |
| Wrecked: "WRECKED – RACE OVER"; spectate (TV cameras, ×2 speed) or skip | ☑️ | `EventHud.ShowWrecked` ("WRECKED – YOU'RE OUT" in the Bowl since M8, "WRECKED – DNF" in a race); `SpectateNext`, `ToggleDoubleSpeed`, `SkipToResults` in both modes. Skip simulates the rest at full speed behind a progress line (`FastFinish`); in a race it is the default after 5 s on a TV shot of your wreck, and spectating names the car you watch (post-M8) |
| The ×2 leader is the event points leader, with a gold beacon | ✅ ✱ A4 | `Damaging_the_bowl_points_leader_doubles_the_spin`, `Bowl_leader_is_unique_positive_points_leader` |
| Opening crunch: a ring facing the centre; everyone meets within ~3 s | ✅ | first contact 1.7 s (M4–M6 derby sims; target < 3 s) |
| No slow motion in the opening crunch | ☑️ | slow motion only when the player wrecks a car or is wrecked (`DerbyPresenter.TrySlowMo`) |
| Alternative start in two facing rows | ☑️ | `DerbyMode.FaceOff`; opened by winning Division 1 |
| Your front is your life; the tough rear makes reverse-ramming a real tactic | ✅ | `Rear_is_much_tougher_than_front`; AI `ReverseRam` with a weak front |
| No single strategy wins more than 40% (hide, pure ram, reverse only, spin-farm) | ✅ | M4 bots: hide 10%, ram 0%, spin-farm 0%, reverse only 48% (missed, open since M4) |
| A 360° spin (50) is worth more than a wreck (25) | ✅ | `Standard_spin_points_match_DD2_PC_manual`, `Wreck_points_per_ruleset` |
| Hunt the leader: AI vultures and grudge-holders go for the ×2 leader | ☑️ | `DerbyBrain` target utility weighs the ×2 leader and revenge |
| The arena evolves: smoke, debris, wrecks as cover, ruts | ☑️ | M3 debris; M4 ground marks; `DerbyBrain` UseCover |
| Escalating finale at 5, 3 and 2 cars; the final-duel banner | ☑️ | `DerbyPresenter`, `IntensityChanged`, floodlight flare (M4) |
| Player-attention budget: 2 / 3 / 4 AI by division | ✅ | wrecked by ≥ 3 attackers in 1–2% (M4–M5 sims; target ≤ 10%) |
| Anti-stall director while 5 or more cars remain | ✅ | wakes passive cars after 4 s without a hit (plan: 6 s); longest idle stretch 6.3–6.6 s (target ≤ 8 s) |
| Camera fix: adjustable chase distance and a high "arena" view | ☑️ | Chase (DD2), ChaseFar (DD1) and the high ARENA view (`CameraRig.View.Arena`, 13.5 m back and 9 m up; the camera key cycles to it, M8) |
| Too easy → division-scaled AI | ✅ ✱ A25 | M7 championship sims |
| Total Destruction: every AI hunts you; score = seconds survived + points | ☑️ | `DerbyMode.TotalDestruction`; the best score is kept in `Profile` (M7) |
| Duel: 1 v 1 against one CPU rival | ☑️ | `DerbyMode.Rival` (M7) |

### §4.2 Wreckin' Racing

| Rule | Status | Evidence |
|---|---|---|
| 20 cars; a fixed number of laps per layout (data-driven; DD2's 5–8 min races) | ☑️ ✱ A34 | oval 14 laps, figure-8 10 laps (post-M8; 8 and 6 in M6) |
| Total = spin and wreck points + finishing bonus | ✅ | `Finish_tables`; `RaceMode.EndRace`; Wreckin' sims (M1c–M6) |
| One pit stop per race | ☑️ | `RulesConfig.WreckinPitStopsAllowed = 1`; the HUD says when none is left |
| Wrecked cars stay on the track as obstacles | ☑️ | M6: the AI steers round them or shoves them aside |
| Flipped cars are righted after a penalty | ☑️ ✱ A13 | righted at 3 s, on the road (`RaceMode.RightingPoseFor`) |
| ×2 for hitting the car in 1st, shown by the leader beacon | ✅ | `Wreckin_race_leader_comes_from_the_mode`; leader spun 2.0 per race (M6) |
| Racer, hunter and leader-killer are all viable | ✅ | racer vs hunter bots 44 vs 39 points, 11% apart (M1c; target ±15%) |
| Blind figure-8 crossover; the AI brakes, gambles or aims by personality | ✅ | 23 crossover incidents per race (M6; target ≥ 2) |
| The walls open at the crossover, with flared ends | ☑️ ✱ A23 | M1c grey box; M6 Speedway |
| Layouts 3 cars wide | ☑️ | roads 13 m wide (M6) |
| Laps of 35–55 s | ☑️ | race laps 31–37 s (oval) and 38–42 s (figure-8); the short oval was accepted at M6 |
| Roof colours show your division rivals | ☑️ | M7 championship events |
| Last-lap drama: banner, announcer, crowd | ☑️ | FINAL LAP banner; `RaceCommentary` (M6) |
| Infield road-course layout (a Should, §19) | ⏳ | not built (M6, M7); no milestone set |

### §4.3 Stock Car Racing

| Rule | Status | Evidence |
|---|---|---|
| 20 cars; points for the finishing position only | ✅ | `Finish_tables`, `Stock_car_scores_no_damage_points` |
| Contact is allowed but scores nothing | ✅ | `Stock_car_contact_never_scores` |
| Damage still applies; a wreck is a DNF | ✅ | `Finishing_and_retirements_rank_correctly`; 1.0–1.5 DNFs per race (M6) |
| Pit as often as you like, 5 s per stop | ☑️ | `StockCarPitStopsAllowed = -1`, `PitStopTimeLimit = 5` |
| The AI drives faster than in Wreckin' (DD1) | ☑️ | ×1.03 (`RaceMode.StockCarPace`); the value is not in §20.2 |
| Bruisers still bump; racers race clean | ✅ | bumping from lap 3 at 50–60% of Wreckin' aggression; DNFs 1–4 met (M6) |
| AI pace tuned per division, for close finishes | ✅ ✱ A25 | M6: lap-2 spread 6.6 / 8.9 s (target ≤ 6 s, missed); late battles 34% / 50% |

### §4.4 Time Trial

| Rule | Status | Evidence |
|---|---|---|
| Solo, with a stopwatch at the bottom of the screen (DD1) | ☑️ | `TimeTrialMode`, `TimeTrialHud` |
| Best laps kept per layout, direction and car | ☑️ | top-10 board per layout, direction and car (M6) |
| Bronze / silver / gold targets | ✅ | `TimeTrialSim`: gold reachable by the skilled bot, not the average one, for all cars but the Homestead on the oval forward |
| Medals unlock liveries | ☑️ | `Profile.BestMedal`: Time Trial medals open the bronze, silver and gold colours in the garage, up to your best medal (M8) |

### §4.5 Championship

| Rule | Status | Evidence |
|---|---|---|
| 20 drivers in 4 divisions of 5; all 20 race, ranked within the division | ✅ | `Rejects_wrong_division_sizes`; `Career.RecordEvent`; M7 championship sims |
| Each car's roof colour shows its division | ☑️ | M7 hub and events |
| Wreckin': 4 track events + the Bowl as the finale | ✅ | `A_wreckin_season_is_four_track_events_and_the_bowl` |
| Stock Car: 4 track events | ✅ | `A_stock_car_season_is_four_track_events` |
| The top of your division goes up | ✅ ✱ A6 | `Topping_the_division_promotes_and_the_new_season_uses_the_new_calendar` |
| Otherwise you repeat the same tracks | ✅ | `Missing_the_top_spot_repeats_the_division_and_counts_the_attempts` |
| The player is never relegated | ✅ | `Player_is_never_relegated_in_standard_rules` |
| Divisions stay at 5: AI drivers swap up and down each season | ✅ | `Topping_the_division_promotes_the_player_and_sizes_stay_constant`, `Not_topping_the_division_repeats_the_season` |
| Each promotion opens new layout, direction and light combinations | ✅ | `Each_promotion_opens_new_combinations_and_all_twelve_are_open_at_the_top` |
| Winning Division 1 opens a bonus variant and a livery set | ☑️ | face-off start and eight metallic colours (`Profile.ChampionOpen`); `Winning_division_one_makes_the_player_champion` tests the title only |
| The Bowl finale keeps seasons alive | ✅ | decided before the finale in 11–27% of seasons (M7; target ≤ 30%) |
| 19 named drivers with grudges; text radio taunts | ☑️ | M5 roster; `Taunts` (30 s cooldown per driver, frequency setting) |
| Voiced taunts | ⏳ v1.1 | text only |
| Readable stakes before an event; points tally and promotion sequence after | ☑️ | M7 hub, tally and season review |

### §4.6 Practice and Multi-Player

| Rule | Status | Evidence |
|---|---|---|
| Race Practice: any layout, direction, light, car and AI count | ☑️ | M7 Practice: the Bowl or the Speedway, 0–19 AI, laps |
| A free damage test in the Bowl | ☑️ | parked targets (`DerbyMode.ParkedTargets`) |
| Hot-seat Multi-Player | ⏳ v1.1 | not built |

### §4.7 The Destruction Derby Code (T1–T5)

| Rule | Status | Evidence |
|---|---|---|
| T1 brake-check a tailgater: works for the player (scenario ≥ 60%) | ✅ | `DdCodeTest` T1 10/10 (Kitsune, Brougham; M6) |
| T1: the AI uses it, and low-skill AI fall for it | ✅ | 1–4 brake-checks per race, 30–40% catch the tailgater (M6 sims) |
| T2 turn into the overtaker's rear corner: works for the player | ✅ | 10/10 Kitsune and Brougham; Volant 0/10, below 60% (M6) |
| T2: the AI uses it; skilled AI dodge it | ✅ | `RaceBrain` turn-ins; 15–20 dodges per race (M6); `DerbyBrain` hunts rear quarters |
| T3 ram a car braking for a bend: works for the player | ✅ | 10/10 (M1b–M6) |
| T3: the AI uses it; naive AI brake predictably (bait) | ☑️ | `RaceBrain` T3 taps and predictable braking; the sims don't count T3 |
| T4 squeeze into walls and wreckage: works for the player | ✅ | 10/10 Kitsune, 9/10 Brougham (M6) |
| T4: the AI uses it | ✅ | 2–11 squeezes per race (M6); `DerbyBrain` Squeeze |
| T5 turn at the last moment in a head-on: works for the player | ✅ ✱ A20 | 10/10 Kitsune and Brougham; Volant 3/10, below 60% (M6) |
| T5: the AI uses it | ☑️ | `DerbyBrain` LastSecondTurn; the sims don't count T5 |

### §5 Scoring

| Rule | Status | Evidence |
|---|---|---|
| Point values per ruleset in `RulesConfig` | ✅ | `ScoringTests` |
| ×2 if the victim is 1st at the hit (race position 1; the Bowl's points leader), for spins and wrecks | ✅ ✱ A4 | `Damaging_the_bowl_points_leader_doubles_the_spin`, `Wreckin_race_leader_comes_from_the_mode` |
| Event total = min(cap, spins + wrecks + bonus) | ✅ | `Event_cap_is_applied` |
| Tie-break: more wrecks, then better best finish | ✅ ✱ A10 | `Tie_break_prefers_more_wrecks_then_best_finish` |
| HUD popup stack: "SPIN 180 +25", "LEADER ×2", "WRECKED +25" | ☑️ | `EventHud.Popup`; M4 screenshots |
| World-space popups over the cars | ☑️ | `WorldPopup` over the car that was spun or wrecked, as well as the HUD stack (M8; the derby clip frames) |
| The points total rolls up | ☑️ | `EventHud` counter roll-up |
| Tally screen by category | ☑️ | results show spins, wrecks, bonus and points (`EventHud.ResultRow`) |
| Hit energy from pre-solve velocities: E = ½·μ·v_n² | ✅ | `Rear_end_hit_attributes_the_faster_car_and_uses_reduced_mass`, `Spinning_body_adds_point_velocity` |
| The attacker drives harder towards the other; an even head-on makes both attackers | ✅ | `Equal_head_on_counts_both_as_attackers`, `T_bone_attributes_the_moving_car` |
| A hit needs E ≥ E_HIT (3 kJ, about 11 km/h closing) | ✅ ✱ A16 | `Weak_contact_is_not_a_hit` |
| Spin window 1.0 s; yaw over 60°/s; closes 0.35 s after rotation stops, or at 5 s | ✅ | `Rotation_starting_too_late_is_not_attributed`, `Max_duration_closes_a_never_ending_spin` |
| Tiers 90° / 180° / 360°; the popup upgrades live; the highest tier is awarded | ✅ | `Awards_tier_for_rotation_after_hit`, `Live_tier_upgrades_are_reported_in_order` |
| Spin points to the last attacker; a steal needs ≥ SPIN_STEAL_E | ✅ | `Big_second_hit_during_spin_steals_attribution`, `Small_second_hit_during_spin_keeps_attribution` |
| Wall spins and self-spins score nothing | ✅ | `Wall_spins_and_light_scrapes_score_nothing`, `No_hit_means_no_spin_points` |
| Wreck credit: most damage in the last 5 s; none for walls | ✅ | `Most_damage_in_window_gets_the_wreck`, `Old_damage_does_not_count` |
| Anti-farming: hits on wrecked cars score 0 | ✅ | `Hits_on_wrecked_cars_score_nothing` |
| Anti-farming: spins count only if the victim was driving | ✅ | `Hitting_a_parked_or_wrecked_car_opens_nothing` |
| Anti-farming: hits on stationary cars score 0 | ✅ plan §5 changed | spins only (`Hitting_a_parked_or_wrecked_car_opens_nothing`); wrecking a stopped car that is still running scores the wreck, as finishing off a stuck car did in DD (M8 decision, for your review) |
| Unit tests replay recorded traces, incl. spins during rollovers and 3+-car pileups | ✅ | scripted traces of both (M8): `In_a_pileup_the_strongest_simultaneous_hit_takes_the_spin`, `In_a_pileup_a_later_hit_before_the_rotation_still_takes_the_credit`, `A_rollover_is_not_a_spin`, `A_car_spinning_on_its_roof_scores_like_any_spin` |

### §6 Damage

| Rule | Status | Evidence |
|---|---|---|
| Zones Front, Rear, Left, Right + 4 wheels on the HUD diagram, green → black | ✅ | `Zone_classification`, `Corner_hits_reach_the_nearest_wheel`; `EventHud` diagram |
| Damage is set by impact speed and angle | ✅ | energy from the normal component; `Corner_hits_split_by_push_direction` |
| Front destroyed → radiator gone → wrecked ("Race Over") | ✅ | `Destroyed_front_wrecks_the_car`, `Destroying_the_front_wrecks_and_credits_the_car_that_dealt_most_damage` |
| Sides pull towards the damaged side, smoothly; both sides gone → lock about 35% | ☑️ | `Car`: pull up to 4°, eased at 6°/s; `CarTuning.BothSidesLockScale = 0.35` |
| The rear is very tough | ✅ | `Rear_is_much_tougher_than_front` (2.5×) |
| Heavy rear damage: up to −30% acceleration and top speed | ☑️ | `CarTuning.RearDamagePowerLoss = 0.3` |
| Below 25% rear health, rear wheels can come off (Standard) | ✅ | `Rear_hits_reach_the_rear_axle_once_the_rear_is_ruined` |
| Lost wheels are never repaired | ✅ | `Pit_repair_restores_zones_but_never_lost_wheels` |
| Near destruction: the hood flies off and the engine catches fire | ☑️ | M3: hood off below 8% front, fire below 8% front |
| Front: power falls with health; steam < 60%, grey smoke and stutter < 35%, black smoke, misfires and a loose hood < 15%, fire < 8% | ☑️ | M3 smoke and fire thresholds; stutter in `Car`; the hood works loose below 20% (M3) |
| Wheels wobble; at 0 the wheel comes off and the hub drags (sparks, pull) | ☑️ | M1b wheel damage; M3 hub sparks; M4 hub gouges |
| Flip: 2 s on the roof or side → righted at 3 s; on track at the nearest track point, in the Bowl in place | ☑️ ✱ A13 | `Car` flip timer; `RaceMode.RightingPoseFor`; no righting pose in the Bowl |
| Immobilised for 10 s = wrecked | ✅ ✱ A9 | `Car_that_cannot_move_for_ten_seconds_is_wrecked`; derby sims: no time-outs |
| Pinned (trying to move, still within 1.5 m) = wrecked | ✅ ✱ A9 | `Car_rocking_in_place_is_wrecked_but_a_driving_car_is_not`; the code waits 20 s (`PinnedWreckTime`), A9 says 10 s |
| Pits: the pit lane guides you into the garage | ☑️ | `PitStops` (approach, lane, box) |
| Pits: the camera orbits; ←/→ picks a zone; mash Enter or Space | ☑️ | `RaceMode._UnhandledInput` (M6) |
| Pits: 5 s limit, then out; control back after about 2 s | ☑️ | `PitStopTimeLimit = 5`, `PitStops.ManualHandBack = 2` |
| Pits: a pit lane per layout (data-driven) | ☑️ | each layout has its own lane with four boxes (M6) |
| Repair: PIT_REPAIR_PER_PRESS per press; key echo ignored; at most 15 presses a second | ☑️ | 3.5 per press, half on the wheels (`RulesConfig`, `PitStops.Press`) |
| Damage per hit = k·max(0, E − 6 kJ)^0.8 / (zone × car toughness); knee at 25, cap 45 | ✅ ✱ A14 | `Small_hits_cause_no_damage`, `No_single_hit_takes_more_than_the_cap` |
| The zone comes from the contact point and normal, with spill-over on corners | ✅ | `Corner_hits_spill_into_the_neighbour_zone`, `Flat_face_hits_go_to_that_face` |
| Both cars take damage: ramming with your front costs engine | ✅ | `Front_rams_cost_the_attacker_front_health` |
| One-sided ram: the attacker takes 70%; head-on: both take it all | ✅ ✱ | 40 km/h T-bone probe: victim's side −14, attacker's front −15 (M4); this ✱ has no A-number |
| Zone toughness: Front 1.0, Sides 1.2, Rear 2.5 | ✅ | front and rear: `Rear_is_much_tougher_than_front`; the sides are 1.5 in `RulesConfig`, not 1.2 |
| Grinding: repeat contact within 0.8 s deals 30%; sliding above 20 km/h grinds; the squeezer gets the credit | ✅ ✱ A17 | `Grinding_contact_deals_less_damage_than_a_fresh_hit`; scrape credit (`Referee.OnScrape`) not unit-tested |
| Damage on a destroyed zone: half of it reaches the front | ✅ ✱ A18 | `Destroyed_rear_alone_does_not_wreck_but_further_hits_reach_the_engine` |

## Additions

Every addition that DD1 and DD2 did not have. "On" means it stays on in the Purist preset; "off" means Purist turns it off.

| Addition | Class (§3) | Purist |
|---|---|---|
| Graphics: baked lighting with three light presets per venue, the car paint shader, the Bowl and Speedway art | Presentation | on |
| Layered deformation (crush, dents, folds), detachable parts, glass and lamps | Presentation | on |
| Car detail (post-M8): the venues reflected in the paint, chrome and glass (reflection probes), tyre sidewall lettering, brake discs and drums, dust that builds up during an event, emblems, a model script and a swaying whip aerial | Presentation | on |
| VFX: smoke, fire, sparks, debris, dust, ground marks, oil trails | Presentation | on |
| A crowd of about 1,500 that reacts to hits and the shrinking field | Presentation | on |
| Audio: synthesized granular engines, 45 effects, a mix with ducking | Presentation | on |
| Music: eight tracks that build as the field shrinks; your own music folder | Presentation | on |
| Announcer: 509 lines (345 derby, 106 race, and since M8 58 for the Duel, Total Destruction, the championship and Time Trial); your own recordings can replace any line | Presentation | on |
| Cameras: four views and look-back; field of view and shake settings; TV cameras when spectating | Presentation | on |
| Replays: V at the results, TV, chase and free cameras, slow motion; the damage and the flying parts replay as they happened; H for a highlights reel of the three biggest moments (M8) | Presentation | on |
| UI: broadcast HUD, popups, event feed, banners, hunters count, PIT prompt, WRONG WAY | Presentation | on |
| Front end: menus, showroom, car select with DERBY / WRECKIN' / PACE verdicts, venue preview | Presentation | on |
| Juice: camera punch, chromatic flash, floodlight flare; a zoom-blur pulse on huge hits (M8) | Presentation | on |
| Post (M8): a colour grade per light, film grain and vignette (FILM LOOK), motion blur (40%), depth of field in the TV and replay cameras and behind the menus, rust from a livery's age | Presentation | on |
| Liveries, fictional sponsors, primer panels, champion colours | Presentation | on |
| Radio taunt banners with the driver's number badge | Presentation | on |
| Key presets (Classic DD PC, Arrows, WASD), chosen at the first launch | Rule-neutral QoL | on |
| Rebinding (a key in use swaps over; a Shift warning) | Rule-neutral QoL | on |
| The keyboard test (ghosting) | Rule-neutral QoL | on |
| Restart from the pause menu and the results; R in Time Trial | Rule-neutral QoL | on |
| Time Trial: the ghost of your best lap, three sector splits, medals, a top-10 board, instant restart | Rule-neutral QoL | on |
| Settings: display, graphics, sound, camera, announcer and taunt frequency | Rule-neutral QoL | on |
| Three save slots; a championship saves after every event | Rule-neutral QoL | on |
| Unlock everything (Options → Gameplay) | Rule-neutral QoL | on |
| The Purist switch (Options → Gameplay): the four assists off in one go | Rule-neutral QoL | it is the switch |
| Pit-lane ghosting: cars in the pit lane never touch the traffic | Rule-neutral QoL | on |
| Cool-down lap: finished cars drive round as see-through ghosts | Rule-neutral QoL | on |
| The best Total Destruction score is kept | Rule-neutral QoL | on |
| Opt-in suppression of the Sticky Keys shortcut (plan §14) | Rule-neutral QoL | not built (deliberately: it changes a Windows setting; on request) |
| Counter-steer assist: off / low / high (default low) | Gameplay-affecting assist | off |
| Traction control: on / off (default on) | Gameplay-affecting assist | off |
| Back-marker catch-up: AI cars far behind the leader drive up to 6% faster; never the player or the leaders | Gameplay-affecting assist | off |
| Slow motion when you wreck a car or are wrecked in the Bowl (at most every 10 s) | Gameplay-affecting assist | off |
| Optional handbrake binding | Gameplay-affecting assist | not built |
| SPIN_ASSIST (1.5) and post-hit grip loss (0.8 s): a tapped rear spins, as in DD | Calibration | on |
| DD cornering at speed (✱ A33, post-M8): grip ×2.3 from 108 km/h and a yaw control that holds the turn the keys ask for and stops it when they come up; the brake loosens it (DD2's brake-dab powerslide) | Calibration | on |
| DD2's tail-out slide (✱ A35, post-M8): the car on screen turns past the physics body in fast turns, by up to 24°; only the look | Calibration | on |
| Opening-crunch damage ramp (✱ A19): ×0.4 at GO to ×1 over 90 s since M4 | Calibration | on |
| Race damage ×0.32 (✱ A21, A34: ×0.5 until DD2's race length) | Calibration | on |
| Division pace and aggression (✱ A22, A25): pace ×1.0 → ×1.045 (×0.97 → ×1.015 until post-M8), aggression ×0.8 → ×1 | Calibration | on |
| The attacker's share of contact damage, 0.7 in one-sided rams (M4) | Calibration | on |
| Stock Car AI pace ×1.03 against Wreckin' | Calibration | on |
| DD2 Fast Steer loosens the rear at derby speeds (✱ A20) | Calibration | on |
| Keyboard steering filter (plan §14.3; not named in §3) | Calibration | on |
| Crash cushions: water barrels on pit-wall noses and soft island tips deal 30% of the damage (✱, not in §20.2) | Calibration | on |
| Recovery: a car stuck off the road for 10 s, or 3 s inside an infield island, goes back on the track | Calibration | on |
| A car resting on another car slides off (M3) | Calibration | on |
| Slipstream: −35% drag up to 25 m behind another car (M6; not named in §3) | Calibration | on |
| A mixed EU / JP / US roster of nine cars with differing stats (K5) | Deliberate change | on |
| Original venues, drivers, sponsors and car names: no DD or real-brand IP (§3, rule 4) | Deliberate change | on |

In Purist, the AI cars keep their default counter-steer and traction control: `GameSettings.ApplyAssists` changes only the player's car.

## Reference feel

Measured from DD1 and DD2 PC gameplay videos (`docs/reference_feel.md`); ours as that sheet and PROGRESS.md report them.

| Measure | Reference (DD footage) | Ours |
|---|---|---|
| 0–100 km/h | ~2.2 s (DD2) | 2.36 s (M1); 2.2–2.7 s across the nine cars (M5) |
| 0–160 km/h | ~4.6 s (DD2) | 4.74 s (M1) |
| Speed after 14 s at full throttle | ~230–240 km/h (DD2) | 230 km/h (M1); 216–240 km/h across the nine cars (M5) |
| Top speed on a long straight | ~240 km/h (242 KPH seen, DD2) | about 255 km/h from the drag setting (M1 tuning) |
| Mid-corner speed | 150–200 km/h typical (DD2); 170–230 km/h in Chalk Canyon | the AI takes Crossroads' 70 m corners at 131 km/h, the car can hold about 143 km/h (post-M8; about 80 km/h in the M1c figure-8); not the same tracks |
| Cornering at speed | 2.2–2.4 g, steady from 100 to 170 km/h (DD2) | 2.2–2.3 g at 130, 175 and 208 km/h (post-M8 step test); 0.8 g in M1–M8 |
| Turn response | the turn follows the keys within about 0.1 s and stops when the car straightens (DD2) | the turn builds up within 0.2–0.3 s of the key going down (the keyboard filter ramps the lock) and stops 0.21–0.27 s after it comes up (post-M8); M1–M8 kept turning for 2.2 s at 130 km/h, or spun |
| Wrecks in a race | DD2: 0, 5, 9 and 5 of 20 in four races (4.75 on average), mostly in the second half | Wreckin' 4.8 (oval) and 6.7 (figure-8) of 20 per race; the bot is wrecked before half distance in 3–9% of races (post-M8 sims) |
| Launch | 1.3–1.4 g (DD2); ~1.1 g (DD1 Bowl) | not measured in g |
| The Bowl: GO to the first centre pile-up | ~2.5 s (DD1) | first contact 1.7 s (derby sims, M1b–M6) |
| Steady cornering at 60 km/h, full lock | not measurable from video | 0.84 g, ~32 m radius |
| Braking | not measured | 179→0 km/h in 103 m (≈ 1.15 g) |
| Default chase camera (car width / bumper / roof / horizon, share of the picture) | DD2: 31% / 89% / 58% / 50% | 31% / 87% / 56% / 49% |
| Far chase camera (car width / bumper / horizon) | DD1: 19% / 72% / 46% | 19% / 69% / 47% |
| Camera behaviour | follows the body's heading at a fixed distance | low, at a fixed distance (M1) |
| Field | DD1 16 cars; DD2 20 cars | 20 cars |
| Race length | DD1: 12 laps of ~15 s, ~3.5 min; DD2: 5, 8 or 10 laps, ~37.5 s laps, ~5 min | oval 14 laps, figure-8 10 laps (✱ A34, post-M8): the winner needs 368 s and 343 s (Wreckin' sims, medians) |
| Derby length | DD1: ~60 s from GO to "Race Over" (the player in that video) | player survival 90 s, event 181 s (medians, M6 derby sims) |
| A rear-quarter tap spins the target | within ~0.5 s (DD1) | a 12 km/h tap spins an unprepared car 90–180° (T2 10/10); time to spin not measured |
| HUD | DD1 / DD2 layouts: points top left, damage diagram, laps, big position | DD2 layout (M1b), broadcast style since M4 |
| Bowl floor | grey asphalt (DD1) | grey asphalt in the M1 grey box; the final Bowl (M2) has a rutted dirt floor |
| Bowl surroundings | low concrete walls, a banked section, a grandstand, green mountains, blue sky | concrete wall with a tyre barrier, two grandstands, an Alps backdrop (M2) |
| DD2 rollovers and flying parts | not measured yet | – |
| Pit-stop timing | not measured yet | – |
