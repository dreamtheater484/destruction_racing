# Announcer generator

`script.tsv` (the lines) → `generate.py` → `assets/audio/announcer/<id>.ogg` + `manifest.json`.

Each line is spoken by the offline TTS voice **Kokoro-82M** (Apache-2.0, local cache, nothing is downloaded), or taken from your own recording, then put through a numpy **stadium PA chain**: horn loudspeaker, amp grit, compression, slap-back off the far stand, and arena reverb.

## Run it

Double-click **`tools/announcer/generate.cmd`**, or from the project root:

```
.engine\kokoro-venv\Scripts\python.exe tools\announcer\generate.py                    only new or changed lines
.engine\kokoro-venv\Scripts\python.exe tools\announcer\generate.py --force            every line
.engine\kokoro-venv\Scripts\python.exe tools\announcer\generate.py --only big_hit_03  one line (repeat, or a comma list)
.engine\kokoro-venv\Scripts\python.exe tools\announcer\generate.py --check            verify only, synthesize nothing
```

`generate.cmd` takes the same flags. `HF_HUB_OFFLINE=1` is forced, so nothing touches the network. A full run of 345 lines takes about 5 minutes on this PC. The checks run at the end of every run, and the exit code is non-zero if any fail.

Kokoro isn't bit-exact between runs, so a regenerated line can come out with slightly different timing or intonation. `--only <id>` is also the way to re-roll a single take you don't like.

**Incremental:** each manifest entry stores a hash of its text, voice, speed, model and every PA chain parameter (or of the recording file's bytes). A line is re-made only if its hash changed or its file is missing. If you change any value in `CHAIN`, `VOICE` or `SPEED`, everything is redone on the next run. If you remove a line from the script, its `.ogg` (and its Godot `.import`) is deleted on the next run.

## Editing lines: `script.tsv`

Tab-separated: `id<TAB>category<TAB>text`. Blank lines and lines starting with `#` are ignored.

- **Ids** are `<category>_<nn>`, for example `big_hit_03`. The runtime picks a random line from a category and avoids recent repeats, so a new variant is just a new row with the next free number.
- **Write numbers as words** ("number twenty-three"). The generator refuses digits.
- **Avoid ALL CAPS**, which the voice may spell out letter by letter (the generator warns). Use "!" for energy.
- **Keep lines short:** 1-3 seconds, about 15-55 characters. A long line may fail the 4.5 s duration check.
- **The number call-outs** (`wrecked_num_NN`, `winner_num_NN`, `leader_num_NN` for cars 2-61) are the block at the end of the file. The generator rewrites that block from `NUMBER_TEMPLATES` in `generate.py`, so edit the templates there, not the rows. `wrecked_num` alternates between two phrasings by car number.

## Voice

**`am_fenrir` at speed 0.88** (`VOICE` and `SPEED` at the top of `generate.py`).

The four cached male voices were compared on 12 representative lines, including number call-outs and damage calls. Measurements are on the dry Kokoro output. Intelligibility was tested by running the PA-processed audio through Whisper large-v3-turbo, offline from the local cache, as a one-off test; the generator doesn't need it.

| voice @ speed | pitch movement (semitones SD) | loudness swing (dB SD) | voicing clarity (HNR dB) | spectral tilt (dB/oct) | 2-4 kHz share | chars/s | Whisper word errors through the PA |
|---|---|---|---|---|---|---|---|
| am_michael @ 1.12 | 2.86 | 6.18 | 2.9 | -5.1 | 4.9 % | 18.6 | 0 / 79 |
| **am_fenrir @ 1.0** | **3.96** | **6.67** | 4.1 | -4.9 | 4.7 % | 20.9 | 0 / 79 |
| am_fenrir @ 1.12 | 3.99 | 6.61 | 4.2 | -4.9 | 4.8 % | 22.6 | 1 / 79 |
| am_adam @ 1.12 | 2.74 | 6.36 | 2.9 | -4.8 | 4.2 % | 20.4 | 0 / 79 |
| bm_george @ 1.12 | 3.50 | 5.72 | 5.4 | -6.3 | 2.6 % | 16.2 | 1 / 79 |

- **Fenrir is the most energetic.** It has about 40 % more pitch movement than michael or adam and the widest loudness swings, which is the excitable delivery an arena announcer needs. Its voicing is cleaner than michael's or adam's, and it's a touch brighter than michael.
- **All of them survive the PA chain.** Whisper makes 0-1 word errors in 79 words for each voice, so clarity doesn't separate them.
- **George is lively but the darkest**, with the least 2-4 kHz energy, and a 250 Hz-5 kHz horn band punishes that: "sheet metal" was heard as "cheat metal".
- **Fenrir talks fast**, so it runs at 0.88. At that speed it matches the pace of michael at 1.12: about 18.7 characters per second including spaces, about 15.5 letters per second. That's unhurried enough for a 2-second arena reverb.

How each measure was taken: pitch movement is the SD of an autocorrelation F0 track, in semitones. Loudness swing is the SD of the 40 ms frame level. HNR comes from the normalised autocorrelation peak. Tilt is the slope of the third-octave spectrum from 150 Hz to 5 kHz. To audition a different voice, change `VOICE` and `SPEED` and run the generator, which redoes every line. Only these four voices are cached; any other Kokoro voice would need a download.

## The stadium PA chain (`CHAIN` in `generate.py`)

All of it is numpy. The filters are linear-phase FIRs applied by FFT convolution, with their delay removed, so timing is unchanged.

| # | Stage | Settings |
|---|---|---|
| 1 | Trim | 5 ms frames below -45 dB re the loudest frame count as silence; keep 30 ms before the speech and 80 ms after it |
| 2 | Horn loudspeaker | windowed-sinc band-pass, 1023 taps per edge, Blackman window: high-pass 250 Hz, low-pass 5 kHz (about 130 Hz transitions, about 74 dB stop-band); presence bell +4 dB at 2.5 kHz (Gaussian, σ 0.55 octave) |
| 3 | Amp grit | speech set to -14 dBFS RMS, then `tanh(1.5·x)/1.5` soft clip at 2× oversampling (no aliasing), then a 6.5 kHz horn roll-off |
| 4 | Compressor | feed-forward, threshold -20 dBFS, 4:1, 6 dB soft knee, attack 3 ms, release 80 ms (gain smoothed in dB); automatic make-up gain. About 6 dB average gain reduction on speech |
| 5 | Slap-back | far-stand echo at 110 ms, -12 dB, low-passed at 2.5 kHz |
| 6 | Arena reverb | synthetic IR: 40 ms pre-delay, seeded noise in 8 octave bands with exponential decay, RT60 2.0 s up to 1 kHz falling to 0.9 s at 5 kHz (highs die faster), 8 ms diffuse build-up, unit energy; wet 22 % (dry 78 %) |
| 7 | Level | gain set so the dry speech is -16 dBFS RMS on every line (consistent loudness), then a look-ahead peak limiter (2 ms, 60 ms release) at a -1 dBFS ceiling. The reverb tail is cut once it is under -54 dBFS (at most 1.1 s after the speech) and faded out over 120 ms |
| 8 | Output | OGG Vorbis, mono, 24 kHz, via soundfile |

## `manifest.json`

```json
{ "voice": "am_fenrir", "speed": 0.88, "sample_rate": 24000, "chain": { "...": "the CHAIN parameters" },
  "lines": [ { "id": "big_hit_03", "category": "big_hit", "text": "Bang! Right in the door!",
               "file": "big_hit_03.ogg", "duration": 2.06, "speech_end": 1.15, "hash": "..." } ] }
```

`duration` is the whole file, including the reverb tail. `speech_end` is when the last word ends (the tail rings on after it), so the runtime can queue the next call or release music ducking before the file finishes. Entries made from a recording also carry `"source": "recording"`.

## Your own recordings

See `docs/announcer_recording_guide.md`. There are two routes:

- **Drop-in:** put `<id>.wav` or `<id>.ogg` in `user://announcer/` or an `Announcer` folder next to the game. The runtime plays it in place of the TTS line, exactly as recorded.
- **Baked:** put `<id>.wav`, `.ogg` or `.flac` in `tools/announcer/recordings/` and run the generator. It replaces the TTS take, gets the same PA chain and loudness, and is marked `"source": "recording"` in the manifest. The chars-per-second check is skipped for these lines.

## Checks (`--check`, and at the end of every run)

- Every manifest entry has its file, and every script line has an entry.
- Each file is mono 24 kHz and lasts 0.4-4.5 s, with the duration matching the manifest.
- No clipping: the decoded file peaks under -0.1 dBFS. Vorbis can overshoot the limiter by about 1 dB, so the generator decodes every file and re-encodes it slightly lower until the file itself peaks at -1 dBFS (within 0.25 dB).
- No line is silent or too quiet.
- The speech rate is plausible, 8-20 characters per second of speech. This catches TTS output that came out cut short, stretched or garbled. "Too fast" counts letters and digits only, because spaces and "!" can take no time. "Too slow" counts every character, because commas and "!" carry pauses ("Go, go, go!").

The check also prints file counts per category, the total size, and the range of durations, rates and peaks.

Current output: 345 files, 5.3 MB, 15.4 minutes of audio. Files run 1.66-4.26 s; written lines have 0.8-3.5 s of speech (median 1.8 s) and number lines 1.0-2.4 s. Peaks are -3.4 to -0.75 dBFS. Average compressor gain reduction is 6.2 dB, average limiter action 0.3 dB (2.6 dB at most).
