# sfxsynth: synthesized sound effects

Every sound effect in `assets/audio/sfx/` is generated here with Python and numpy. Nothing is recorded or
downloaded, and nothing beyond numpy is needed (no scipy). The engine sounds come from the sibling
`tools/enginesynth`.

## Re-running

From the project root:

```
python tools/sfxsynth/synth.py                       # regenerate everything (about 2 minutes)
python tools/sfxsynth/synth.py --only glass_break    # one id (repeat --only for more); the manifest is updated in place
python tools/sfxsynth/synth.py --spectrograms DIR    # also write one spectrogram PNG per file into DIR
```

The run writes 16-bit PCM mono WAVs at 44.1 kHz plus `assets/audio/sfx/manifest.json` (files, loop flag
and actual duration of each variant per id). At the end it prints a quality table, covered below.

Loop files also carry a standard `smpl` chunk: one forward loop over the whole file. Godot's WAV
importer, set to "Detect From WAV" (`edit/loop_mode=0`, the default), therefore imports them as looping.
A full run deletes WAVs in the folder that are no longer in the sound list.

The output is deterministic. Every (id, variant) pair gets its own numpy generator, seeded from a CRC32 of
`"<id>:<variant>"`, so a full run and an `--only` run produce identical files.

## Files

| Module | Contents |
|---|---|
| `synth.py` | The sound list (ids, variant counts, target lengths, relative gains), mastering, the quality report and the manifest. |
| `dsp.py` | Shared numpy DSP: FFT and circular convolution, zero-phase spectral masks, modal impulse responses, two-pole resonator responses, an STFT time-varying filter, envelopes, Poisson and bounce event timing, a look-around limiter, a synthetic reverb IR, analysis and seam metrics, a `wave` writer and a minimal PNG writer. |
| `impacts.py` | Car-vs-car, tyre barrier and concrete hits; glass, lamps, parts coming off, debris, loose wheels. |
| `loops.py` | Scrape, tyre, hub-grind, fire and steam loops. |
| `crowd.py` | The stadium crowd: a voice model, behaviours (talk, shout, clap, whistle), distance buses and reverb. |
| `ui.py` | Countdown, air horn, stings, whooshes, misfires and UI clicks. |

## Models

- **Car-vs-car hits** (`hit_metal_*`). Each crunch stage has three layers:
  - a thump: a 40-125 Hz sine whose pitch drops as the contact stiffens, plus low noise;
  - modal sheet steel: 1-3 panels, each about 160 inharmonic plate modes with constant modal density
    between 150 Hz and 6 kHz. Decay time falls with frequency, so the highs die first. They are excited by
    a half-sine contact pulse, which only reaches the low modes, and by a beta-distributed cluster of
    sub-millisecond buckling snaps in the first 30-120 ms;
  - a crackle: band-passed noise gated by hundreds of micro-bursts.

  The tiers add more:
  - heavy and huge: a tearing layer (dense stick-slip bursts, a sweeping band-pass, a ringing panel) and a
    loose-panel buzz;
  - medium and up: a rattle tail of small pieces (a click plus 3-5 high modes at thinning random times);
  - huge: three stages and a glass shower.

  Layers are mixed by RMS. A crest limiter keeps the variants of a tier at similar loudness.
- **Tyre barrier** (`hit_tyres_*`). A long, soft half-sine contact into the stack and tyre-cavity modes,
  a rubber slap, a short stick-slip rubber squeak glide, rebound thumps (bounce timing) and the stack
  rocking, with some of the car's own crunch and a faint rattle. On heavy hits a damped thock comes from
  the concrete behind, and a tyre is knocked off the stack and bounces.
- **Concrete** (`hit_concrete_heavy`). A broadband slap with very damped concrete modes, the body thump, a
  heavy metal crunch with tearing, grit chips, a short gritty slide and a rattle tail.
- **Glass** (`glass_break`, `lamp_break`). A fracture click with a few bright modes, then a shower of
  tiny fragments. Each fragment is 2-4 inharmonic modes at 2-12 kHz with 3-40 ms decays; small pieces
  are higher and shorter. The shower has a burst that thins out, a landing wave and a tail.
  - Even-numbered `glass_break` variants are laminated windscreens: a duller thunk, a crackling crack web
    and a smaller shower.
  - Odd-numbered variants are tempered side windows: a dense shower of about 1000-1500 fragments.
  - Lamps: a mid-frequency lens pop, a small shower and plastic crackle.
- **Parts** (`part_off`). A tearing excitation (jittery stick-slip impulses whose rate and strength
  build up) through panel modes and a sweeping band-pass, then the release snap. The part then hits the
  floor (free-free beam modes, ratios 1 : 2.76 : 5.40 : 8.93, plus a panel and a thud) and bounces once.
- **Debris / wheels** (`debris_clatter`, `wheel_bounce`). Bouncing-object timing, where the gaps shrink
  by the coefficient of restitution. Each piece has its own modes, re-weighted per bounce because each
  bounce hits a different spot, plus a ground tick. A wheel is a tyre thud, a cavity resonance and a
  steel-rim ring.
- **Loops** (`loops.py`) are circular signals, which is how they stay seamless. Noise is shaped in the
  frequency domain of the whole loop, modulators are periodic, oscillators complete a whole number of
  cycles, events wrap round the end and resonators and reverbs are circular convolutions.
  - `scrape_metal_loop`: rough noise and stick-slip impulses ringing two slowly crossfaded panels, grind,
    rumble and an intermittent friction squeal.
  - `scrape_wall_loop`: a rubber judder at the tyre pitch with groans, concrete grit and some metal.
  - `tyre_squeal_loop`: two narrow-band squeals (about 1.0-1.9 kHz) with wander, vibrato and jitter FM,
    weak harmonics, a narrow noise band and road roar.
  - `tyre_dirt_loop`: sand spray, pebble ticks, stones on the panels and a gravel crunch under the tyres
    at the wheel rate.
  - `hub_grind_loop`: a five-lug hub rotating with periodic chatter, ringing hub/disc modes, spark
    crackle flares, bumps and hard saturation.
  - `fire_loop`: a turbulent roar, rustle, power-law crackles and fizz.
  - `steam_loop`: jet hiss with pressure spurts, bubbles and sizzle.
- **Crowd** (`crowd.py`). Each of the 300-600 voices is a person with a male or female pitch range, a
  vocal-tract scale, breathiness and a distance. The parts of the model:
  - Source: a Rosenberg glottal-flow derivative on a pitch contour, with vibrato and jitter, plus
    flow-modulated aspiration noise. Vocal effort lowers the open quotient and raises the pitch and F1,
    as shouting does.
  - Filter: a five-formant Klatt-style cascade with Peterson & Barney vowel averages, applied in the
    frequency domain. Diphthongs ("yeah", "hey", "go") crossfade two vowel filters.
  - Behaviours:
    - talkers chain syllables into phrases with fricative and plosive noise;
    - cheering voices shout open vowels on arched pitch contours ("woo" rises);
    - an "ooh" is a rise-fall /u/;
    - a groan is a falling /a/ to /o/;
    - claps are resonant noise bursts per clapper, whistles are breathy glides.
  - Mix: voices go to near, mid and far buses, with duller filtering and more reverb the further away.
    A large-room synthetic reverb (band-split decaying noise, about 2 s) is added. The loops also add the
    far bus again at other time offsets, which reads as the rest of the stands. Voices are rendered at
    22.05 kHz and upsampled per bus, because the crowd has no content above 11 kHz.
- **UI / game** (`ui.py`):
  - Beeps: band-limited square-ish tones with a sub-octave body through a stadium-PA horn response.
  - `air_horn`: three reed-horn notes plus an octave, each two detuned additive sawtooths with an onset
    pitch kick, through horn formants, saturated.
  - Stings: a brass-like additive synth playing rising arpeggios that get higher, brighter and longer
    per tier. Tier 4 ends on a C major chord with a glock sparkle.
  - `sting_wreck`: a kick, a crack and a clang, then a falling saw and noise sweep.
  - `sting_final`: a timpani-like boom, an orchestral-hit burst and a swelling D minor chord with a
    tritone and tremolo.
  - Whooshes: swept band-pass noise (STFT); `slowmo_in` adds a tape-stop tone.
  - `misfire`: a pressure pulse and combustion noise through exhaust-pipe modes (a different pipe length
    per variant), a muffler and arena slap echoes. The variants are a pop, a bang, a double pop and a
    crackle.
  - UI clicks and dings: short modal partials.

## Mastering

| | Steps |
|---|---|
| One-shots | 18 Hz DC blocker, cut to the target length with a 12 % raised-cosine tail taper, peak to -1 dBFS, then the relative gain from the list in `synth.py`. The end is trimmed where the level drops below -60 dBFS, then faded out over 10 ms. There is a 5 ms fade-in, except on sounds that start on their transient (impacts, glass, parts, misfires, clicks, the wreck and final stings), which get only a 0.2 ms click guard. |
| Loops | Circular DC removal, RMS to -18 dBFS, and a circular look-around limiter that keeps peaks below -1 dBFS (gain re-applied and iterated). |

## Quality report

The run prints one row per file with:
- duration, peak, RMS, DC offset and the number of clipped samples;
- the spectral centroid and the share of energy below 150 Hz;
- two seam checks for loops: the end-to-start sample jump relative to the loop's own 99th-percentile
  step, and a z-score of the spectral flux across the boundary against every other frame-to-frame flux.
  A splice or a missing filter tail stands out at z >> 3.

After the table it prints:
- variant distinctness: the maximum cross-correlation between variants;
- the tier scaling of the metal and tyre hits: length, RMS and low-end energy;
- a formant check for the crowd: the prominence of the long-term-spectrum peaks in the F1 and F2
  regions, which white or pink noise lacks.

## Adding or changing a sound

1. Write a generator `(rng, variant, target_seconds) -> numpy array` in the right family module.
   - A loop must return exactly `ns(target)` samples, built circularly.
   - A one-shot may return more samples than the target; mastering cuts it.
2. Add a `Sound(...)` line to `registry()` in `synth.py`.
3. Run it with `--only <id>`.
