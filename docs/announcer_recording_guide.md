# Recording your own announcer lines

The announcer ships with a text-to-speech voice (Kokoro, run offline). You can replace any line, or all of them, with your own recordings. Lines you don't record keep the TTS voice.

## 1. Find the line

Every line has an **id** and a **text**. They're in `tools/announcer/script.tsv` (open it in any text editor or spreadsheet) and in `assets/audio/announcer/manifest.json`. For example:

| id | what to say |
|---|---|
| `player_on_fire_02` | Number seven is on fire! Keep moving! |
| `big_hit_07` | Ouch! Did you hear that? |
| `wrecked_num_23` | That's it for number twenty-three! |

To hear the current TTS take, play `assets/audio/announcer/<id>.ogg`.

## 2. Name and format the file

- **Name:** the id plus `.wav` or `.ogg`, exactly as written in the script, in lower case: `player_on_fire_02.wav`.
- **Format:** mono WAV (16- or 24-bit) or mono OGG Vorbis. Any sample rate is fine (44.1 kHz and 48 kHz are typical).
- **One line per file.** Cut each take to the line itself: start the first word within about 0.1 s, and stop shortly after the last word. The announcer reacts to the action, so a late start sounds slow.

## 3. Put it where the game looks

A recording in either folder replaces the line with the same id. Delete the file to get the TTS line back.

- **Your user folder:** `user://announcer/`. On Windows that's `%APPDATA%\Godot\app_userdata\Destruction Racing\announcer\` (paste that into the Explorer address bar; create the `announcer` folder if it isn't there).
- **Next to the game:** a folder named `Announcer` beside the game's `.exe`. This is handy for sharing a voice pack with friends: zip the folder.

Drop-in files are played as you recorded them. The game doesn't add the stadium PA sound.

**Want the stadium sound on your voice too?** Put the recording in `tools/announcer/recordings/` instead (same name, WAV/OGG/FLAC, any sample rate, mono or stereo) and run the generator (see `tools/announcer/README.md`). It uses your take in place of the TTS, runs it through the same PA chain (loudspeaker, echo, arena reverb, loudness matching), and writes it into the game's files. Record completely dry for this route: no reverb or effects.

## 4. Level and sound

- **Peaks around -6 dBFS** on the meter, and never in the red. A clipped take can't be repaired.
- **Keep the same mic, distance and gain for every line**, so lines match in loudness. If they don't, normalise each file to about -3 dBFS peak in your editor (Audacity: *Effect > Normalize*).
- **Quiet, soft room:** no TV, fan or fridge noise. A room with curtains, a sofa or a wardrobe full of clothes beats a bare kitchen. Background hiss should be well below the voice (around -60 dBFS).
- **15-20 cm from the mic**, slightly off to the side, or use a pop filter. Lines like "Bang!", "Pedal to the metal!" and "Boom!" pop easily.
- **No effects:** no reverb, EQ presets or noise gates, especially for the generator route.

## 5. Delivery

- **Big, but projected rather than screamed.** Picture calling to ten thousand people across a stadium. Stand up, chin up, smile.
- **Punch the key word:** "You're on *fire*!", "There goes your *hood*!", "*Head-on*!"
- **Keep it short and quick.** Most lines should be 1-3 seconds. Don't drag the ends of words, because the arena adds its own echo.
- **Numbers clearly:** "Number *twenty-three* is out!" The car number is the information.
- **Record two or three takes** of each line and keep the liveliest. Vary your pitch between lines in a category so repeats don't sound copy-pasted.
- **Record a whole category in one session** (all `big_hit_*`, all `player_lost_*`, and so on), so your voice and energy match within it. A full set is about 165 written lines plus 180 number call-outs (three phrases for cars 2 to 61).
- **Warm up and drink water.** Shouting lines for an hour is hard on the voice. Take breaks.

## 6. New lines

To add a variant, add a row to `tools/announcer/script.tsv` with the next free number in that category (for example `big_hit_16	big_hit	What a smash!`), then run the generator. It makes a TTS take and a manifest entry for the new line, and you can then record over it as above.
