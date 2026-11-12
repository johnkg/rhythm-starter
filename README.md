# RhythmStarter

Minimal rhythm game. 4 lanes, tap + hold notes, audio-clock sync, calibration, chart recorder,
song select, settings, pause, results screen with rank and best score.
All UI is throwaway IMGUI, swap later. Unity 2022 LTS or newer. Needs **Input System** package.

**Status:** never run in Unity yet. Scripts compile clean against a stub of the Unity API and the
judging, clock and pause logic pass simulated tests (`Tools/LogicTests`), but real audio, input
timing and scene wiring are untested. Expect small fixes on first run.

## Project setup (once)

1. New Unity 2D project. Package Manager: install **Input System**. Accept the restart.
   Project Settings > Player > Active Input Handling = **Input System Package (New)**.
2. Copy `Assets/Scripts`, `Assets/Editor` and `Assets/Resources` into your project. (`Tools/` stays outside Assets.)
3. Menu **Rhythm > Build Starter Scenes**. It makes the note prefab, all scenes, wires every
   component and fills Build Settings. Safe to run again, it rebuilds from scratch.
4. Open `Assets/Scenes/SongSelect`, press Play. Demo song is silent but notes still play.

Prefer to wire by hand (or something went wrong)? Manual steps are under **Scenes** below.
You need a note prefab: GameObject > 2D > Sprites > Square, scale ~(1.3, 0.3, 1), add `NoteView`, save as prefab.
Scene names must match exactly and go in File > Build Settings: `SongSelect`, `Game`, `Calibration`, `Settings`.

## Scenes (what the menu builds, for manual wiring)

**SongSelect**: empty GameObject + `SongSelect`. Reads `Assets/Resources/songs.json`.
Keys: Up/Down, Enter = play, C = calibration, O = settings, Esc = quit.
The highlighted song previews: ~15 s loop with fades, starts 0.4 s after you stop scrolling, loads in the
background so scrolling never freezes. Starts about 35% into the song unless `previewStart` is set.
Songs without an audio file stay silent. Volume is in Settings (0 = off).

**Game**: empty GameObject `Game` with:
- `AudioSource` (leave clip empty, **uncheck Play On Awake**)
- `Conductor`
- `NoteSpawner` (assign note prefab)
- `Judge` (assign NoteSpawner)
- `GameBootstrap` (assign Conductor + NoteSpawner)
- `ResultsScreen` (assign Judge + NoteSpawner)
- `PauseMenu` (assign Judge + ResultsScreen)
- `HitEffects` (assign Judge + NoteSpawner): lane glow, grade popups, combo counter
- `TapSound` (assign Judge): click on every hit. Miss is silent. Pitch: Perfect highest, Good lowest

The menu also adds lane strips and a hit line at y = -3, and a camera sized to fit.
Play keys: **D F J K** (rebindable) or gamepad d-pad / face buttons. Esc = pause.
Pressing Play directly in the Game scene works: no song picked = chart `demo`, silent.

**Calibration**: empty GameObject, `AudioSource` (no clip) + `CalibrationScreen`.
Tap SPACE / A / click to the clicks. 16 taps, median saved. R = redo.

**Settings**: empty GameObject + `SettingsScreen`.
- Note speed 1-10 (7 = 1.5 s travel). Higher = faster.
- Audio offset: fine tune after calibration, 5 ms steps.
- Visual offset: raise if notes look late on your screen. Moves the picture only, judging unchanged.
- Lane keys: Enter on a lane row, press the new key. Esc cancels. A key already used by another lane swaps with it.
  Gamepad layout is fixed. Navigation here is arrows / d-pad only, so any letter can be bound.
- Calibrate row opens the calibration scene.

## Hit sound

`TapSound` plays a short click on each hit (not on a miss), generated in code, no asset needed.
8 voices take turns so fast taps never cut each other off. Volume in Settings (0 = off).
Own sound: put a clip at `Assets/Resources/Sfx/tap.wav` (or .ogg). It replaces the built-in clicks for all grades.
Set the clip to **Load Type = Decompress On Load**, short length, to keep latency low.
It plays when the hit is judged, so it is as accurate as your input timing, but Unity audio output has its own
delay (DSP buffer). Test in a real build with DSP Buffer Size = Best Latency. If the click feels late,
that delay is the cause, and calibration (audio offset) does not fix a sound that plays on press.

## Pause

Esc / gamepad Start. Enter = resume (3 s countdown), R = restart, Q = song select.
Pausing mid-hold drops that hold (no free pass). Pause is refused during the lead-in.

## Calibration notes

- Result includes audio output latency. Bluetooth headphones = big offset. Let player redo any time.
- Fixes audio side. Display lag is the Visual offset setting.
- Spread over ~50 ms = uneven taps, retry.

## Adding a song

1. Audio at `Assets/Resources/Audio/<name>.ogg` (or wav).
   Import: **Load Type = Decompress On Load** for low latency. Resources audio is memory heavy, fine for a few songs.
2. Make chart (recorder below), save to `Assets/Resources/Charts/<chart>.json`.
3. Add entry to `Assets/Resources/songs.json`:
   `{ "id": "mysong", "title": "...", "artist": "...", "bpm": 128, "audio": "Audio/mysong", "chart": "mysong", "level": 3 }`
   `id` must be unique, it is the high score key. `audio` path has no extension.
   Optional `"previewStart": 45` = seconds into the song where the select-screen preview begins (0 = automatic).
4. Demo song has no audio file, so it runs silent. Notes still play.

## Chart format

```json
{ "title": "x", "lanes": 4, "notes": [
  { "time_ms": 2000, "lane": 0, "type": "tap" },
  { "time_ms": 5000, "lane": 2, "type": "hold", "duration_ms": 1000 }
] }
```
- Times are song time. 0 = start of audio.
- `duration_ms` > 0 makes it a hold. Do not overlap notes in the same lane.

## Hold notes

- Press inside window to hit head. Keep key down until the tail ends. Head gives normal score, finish gives +500.
- Let go early = **dropped hold**: combo resets, accuracy penalty, no bonus.
- Still holding at end = completes by itself. Release within 120 ms before end is also OK.

## Chart recorder

1. Scene with `Conductor` + song `AudioSource` + `ChartRecorder`. No spawner or judge needed.
2. Set `chartName`, `bpm`, `firstBeatSec`, `snapPerBeat`, `holdThresholdSec`.
3. Play. Tap D F J K to the song. Hold a key longer than `holdThresholdSec` = hold note.
4. **Enter** saves, **Backspace** undoes last note. Autosaves if you stop play mode.
5. File: `<persistentDataPath>/charts/<chartName>.json` (shown on screen and in Console).
   Windows: `%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\charts`.
   Copy to `Assets/Resources/Charts/`, add to `songs.json`.

Tips:
- Run calibration first. Recorder uses the saved offset.
- Quantize needs correct `bpm` and `firstBeatSec`, else notes snap wrong. Turn off if tempo drifts.

## Chart converter

`Tools/ChartConvert/chartconvert.py` turns other charts into game JSON. Python 3.8+, no packages.

```
python Tools/ChartConvert/chartconvert.py song.osu -o Assets/Resources/Charts/mysong.json
python Tools/ChartConvert/chartconvert.py song.osu -o Assets/Resources/Charts/mysong.json \
    --songs-json Assets/Resources/songs.json --audio Audio/mysong --level 4
python Tools/ChartConvert/chartconvert.py notes.csv -o out.json --title "My Song" --bpm 128
```

- **.osu**: osu!mania beatmaps. Taps and holds, BPM, title and artist are read. 4K works as is.
  5K / 7K etc need `--fold` (squashes to 4 lanes, drops notes that collide).
- **.csv**: `time_ms,lane[,duration_ms]` per line, lane 0-based, `#` comments, header optional.
  Handy for typing a chart in a spreadsheet. See `Tools/ChartConvert/samples/`.
- Cleans the result: holds under 150 ms become taps, notes less than 30 ms apart in one lane
  (100 ms after a hold ends) are dropped because nobody can play them. Tune with `--min-hold-ms`,
  `--min-gap-ms`, `--hold-gap-ms`.
- `--offset-ms -50` shifts the whole chart. Use it if converted charts feel early or late against your audio.
- `--songs-json` adds the song entry for you (skips if the id exists). `--preview-start 45` sets the preview start.
- Only convert charts and audio you have rights to use.
- Tests: `cd Tools/ChartConvert && python3 -m unittest test_chartconvert`

## Song transcriber (audio file to chart, with PDFs)

`Tools/Transcribe/transcribe.py` listens to a song and writes a first-draft chart. Optional PDFs for checking on paper.

```
cd Tools/Transcribe
python3 transcribe.py song.mp3                          # chart JSON next to the song
python3 transcribe.py song.mp3 --pdf                    # + chart PDF + melody sheet PDF + melody MIDI
python3 transcribe.py song.mp3 --pdf --project ../.. --title "My Song" --artist "Me" --level 4
```

`--project` adds the song to the game: audio to `Assets/Resources/Audio/<id>.ogg`, chart to
`Assets/Resources/Charts/<id>.json`, entry appended to `songs.json` (BPM and preview start filled in).
The audio is converted to ogg first and the chart is timed against that file, so the game plays the same
samples the tool measured (`--audio-format keep` copies the file as it is).

Outputs, named after the song id:

| File | What |
|---|---|
| `<id>.json` | chart, same format as the recorder and the converter |
| `<id>_chart.pdf` | the chart on paper: 4 lanes, read bottom to top, bar numbers and times |
| `<id>_melody.pdf` | main melody as simple sheet music. A draft |
| `<id>_melody.mid` | the same melody as MIDI. Open in MuseScore (free) for a clean, editable score |

Useful options: `--level 1..10` (note density: 1-2 beats only, 3-5 eighth notes, 6+ sixteenths, 7+ some double notes),
`--bpm` and `--offset-ms` if you know them, `--no-holds`, `--snap` / `--no-snap`, `--sheet-grid 16`,
`--melody-low C3 --melody-high C6` (where to look for the melody), `--chart-pdf` / `--sheet-pdf` for one PDF only.

What to expect:

- **Chart**: finds the tempo, the beat and the strong hits. Low sounds go left, high sounds go right. Assumes a
  steady tempo (most studio music). If the tempo wanders (live recording, classical) it says so and keeps the raw
  hit times without snapping. Fast songs can come out at half tempo (174 read as 87): notes still land on the
  beat, pass `--bpm 174` to fix bar lines and the sheet. Always play the chart and fix it with the recorder or by hand.
- **Melody sheet**: one note at a time, the most prominent pitch. No chords, no lyrics, no machine learning.
  Works best on a clear lead over a light backing. On dense mixes expect wrong octaves, missed notes, and
  backing notes where the lead rests. It is a starting point to correct by ear.
- Tested with generated audio only (known tempo and melody), not with commercial recordings.

Needs Python 3.8+, `numpy`, `scipy`, `reportlab` (PDFs only), and `ffmpeg` on PATH for anything that is not a WAV:
`pip install numpy scipy reportlab`. Tests: `python3 -m unittest test_transcribe` in `Tools/Transcribe`.
Only use songs you have the rights to use.

## Results

Rank from accuracy: S >= 95%, A >= 85%, B >= 70%, C >= 50%, else D.
Accuracy: Perfect 1.0, Great 0.7, Good 0.4, dropped hold -0.5.
Best score saved per song id in PlayerPrefs.

## Logic tests (no Unity needed)

`bash Tools/LogicTests/run.sh` needs Mono (`mcs`, `mono`). It compiles every script (runtime and the
Editor setup script) against a fake Unity API, then simulates: tap grades, ghost taps, hold complete / drop / auto-complete / miss,
combo, score, accuracy, clock freeze and resume on pause, user offset, pause during lead-in, key rebinding and swap, settings defaults, preview start / length / fade maths, tap sound samples, voice rotation, volume and miss handling, pause on focus loss. It also compiles everything once with `UNITY_WEBGL` defined so the web-only code paths are checked.
Run it after any change to Judge, Conductor or NoteSpawner. It cannot test real audio or input latency.

## Web (WebGL) build

Same project builds for Steam (Windows/Mac/Linux) and the browser. No code fork.
File > Build Settings > WebGL > Switch Platform > **Build And Run** (Unity serves it on localhost).
Project Settings > Player > Publishing Settings: **Compression Format = Disabled** is the easiest for hosting
(Gzip/Brotli need the right server headers). Open the page by URL, not by double-clicking `index.html`.

Handled for web:
- Quit is hidden (a tab cannot quit itself).
- Chart recorder saves by **browser download** (`WebDownload` + `Assets/Plugins/WebGL/RhythmWeb.jslib`). Copy the file to `Assets/Resources/Charts/` and rebuild.
- Tab hidden / window loses focus pauses the game. Not in the editor (it would pause whenever you click the Inspector).
- Settings, best scores and calibration are kept by PlayerPrefs (browser storage). They are per browser and per site address, and clearing site data wipes them.

**Do not judge final timing feel on the web build.** Use it to test flow, visuals, charts, menus, difficulty.
- Browser audio has its own latency and no DSP buffer setting to tune. It varies by browser, OS and output device.
- Keyboard events may only be seen once per frame, so press timing could be limited to frame rate (~16 ms at 60 fps). Not verified.
- Calibration and the offsets in Settings help with a steady delay, not with jitter.
- Audio only starts after the first click or key press (browser rule). The first song preview may stay silent until then.
- No touch controls yet. Keyboard and gamepad (browser Gamepad API) work through the Input System.
- A public web build is distribution. Only use music you have rights to.
- Size: keep the audio small (compressed). Large clips make slow loads and can run into browser memory limits.

Steam timing is the real test: Windows desktop build, DSP buffer = Best Latency, wired headphones.

## Deployment (free, automatic)

Every push to GitHub runs `.github/workflows/ci.yml`:

1. **Logic tests**: C# suites, chart converter, transcriber. A failed test stops the run.
2. **WebGL build**: Unity runs on GitHub's servers (GameCI), builds the scenes with the same code as the
   menu **Rhythm > Build Starter Scenes** (`Assets/Editor/RhythmCI.cs`), then builds the web player.
3. **Publish** (branch `main` only): the build goes to GitHub Pages.

Live game: **https://johnkg.github.io/rhythm-starter/**  
Other branches build too and keep the result for 7 days as a download (`webgl-build`) on the run page, without publishing.
Cost: nothing while the repository is public (GitHub Actions and Pages are free for public repositories).

One-time setup, only the repository owner can do it:

1. **Unity license (free Personal)**. On any computer: install Unity Hub, sign in, then Preferences > Licenses > Add >
   "Get a free personal license" (do this even if a license is already listed, it creates the file). Find the file:
   Windows `C:\ProgramData\Unity\Unity_lic.ulf`, Mac `/Library/Application Support/Unity/Unity_lic.ulf`,
   Linux `~/.local/share/unity3d/Unity/Unity_lic.ulf`. You do not need to install a Unity editor for this.
2. On GitHub: repository **Settings > Secrets and variables > Actions > New repository secret**, three times:
   `UNITY_LICENSE` = the whole text of that file, `UNITY_EMAIL` = Unity account email, `UNITY_PASSWORD` = Unity account password.
3. **Settings > Pages > Build and deployment > Source = GitHub Actions**.
4. **Actions** tab > "Build and deploy" > **Run workflow** (or push any commit).

Until step 2 is done the workflow only runs the tests and says the web build was skipped.

Notes:

- Unity version for the build is in `ProjectSettings/ProjectVersion.txt` (6000.0.36f1, Unity 6). Packages are in `Packages/manifest.json`.
- First build takes roughly 15 to 30 minutes, later ones less (import cache).
- Scenes, the note prefab and `.meta` files are created during the build and are not in the repository.
  Once you open the project in a real Unity editor, commit the `.meta` files it creates.
- The web build is uncompressed on purpose: GitHub Pages cannot send the headers compressed Unity builds need.
- This pipeline has not completed a real run yet. The first runs are also the first time the code meets a real
  Unity compiler, so expect a few rounds of fixes. Failed run logs show the exact Unity error.

## Gotchas

- Project Settings > Audio > DSP Buffer Size = **Best Latency**.
- `Rhythm > Build Starter Scenes` is untested in a real editor. If a step fails it logs the reason; fall back to the manual wiring.
- Unity Editor adds input lag. Test in a real build before trusting feel.
- Retry reloads the Game scene. Calibration and settings stay.
- Preview loads audio with `Resources.LoadAsync`. Check it in a real build: a few songs are fine, a big library needs Addressables or streaming.
- Pause uses `AudioSource.Pause/UnPause`. Check in a real build that audio and notes stay locked after resume.

## Next steps

1. Real UI (UI Toolkit or uGUI) replacing all IMGUI.
2. Particle effects, per-lane hit sounds, hold loop sound.
3. Steamworks build, Steam Deck test.
4. Switch: audio via Resources is heavy, move to Addressables or streamed audio. Check Nintendo requirements.

## Switch-ready habits

- All input goes through Input System actions, no raw mouse.
- Charts and songs load from `Resources`, not file paths.
- No PC-only plugins.
