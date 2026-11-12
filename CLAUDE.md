# RhythmStarter

Unity rhythm game. 4 lanes, tap + hold notes. Steam first, Switch possibly later.
The owner tests it as a **WebGL web app**, and ships to Steam. One codebase, both targets. Keep every change working on both.
Owner is a .NET dev (C# is home turf). Keep answers terse.

## Status

**Never run in Unity.** Everything was written and checked without an editor. Expect compile fixes and wiring
fixes on first run. When the owner reports errors, fix those first before adding features.

Built: audio-clock sync (Conductor), judging with input event timestamps (Judge), hold notes, calibration,
chart recorder, song select with previews, settings (note speed, audio/visual offset, rebindable keys,
volumes), pause with countdown, results with rank and best score, hit effects, tap sound,
one-click scene builder (menu Rhythm > Build Starter Scenes), chart converter (.osu, .csv),
song transcriber (audio file to chart JSON, chart PDF, melody sheet PDF, MIDI).
Web timing work in progress: `SmoothClock` + `AudioTimeline` (audio position mode on WebGL) are in and tested;
JS key timestamps, hit stats / F3 overlay and note pooling are not built yet.
All UI is throwaway IMGUI.

## Layout

- `Assets/Scripts/` runtime code. `Assets/Editor/RhythmSetup.cs` scene builder. `Assets/Resources/` songs.json, charts, audio.
- `Tools/LogicTests/` C# logic tests, no Unity needed. `Tools/ChartConvert/` Python converter + tests.
- `Tools/Transcribe/` Python transcriber: `transcribe.py` (CLI), `rhythm.py` (tempo, chart), `melody.py` (pitch line),
  `chart_pdf.py`, `sheet_pdf.py` (own staff drawing, reportlab), `midi.py`, `synth.py` (test audio). Uses `chartconvert` helpers.
- `README.md` has setup, scene wiring, chart format, how to add songs. Keep it in sync with changes.

## Run the tests (do this after every change)

```
bash Tools/LogicTests/run.sh                                    # needs mono: sudo apt install mono-mcs mono-runtime
cd Tools/ChartConvert && python3 -m unittest test_chartconvert
cd Tools/Transcribe && python3 -m unittest test_transcribe     # needs numpy scipy reportlab, ffmpeg for the mp3 test
```

`run.sh` compiles every runtime script and the Editor script against fake Unity APIs in
`Tools/LogicTests/UnityStubs.cs` and `EditorStubs.cs`, then runs simulations. If you use a Unity API that is not
in the stubs, add it there (fake, minimal). Stubs prove compile-level correctness only, not real Unity behavior.

## Rules that matter

- **Time**: song time comes from the audio clock via `Conductor` -> `AudioTimeline` -> `SmoothClock`
  (`AudioSettings.dspTime` on desktop, `AudioSource.timeSamples` on WebGL), never frame time.
  Input uses `ctx.time` event timestamps converted by `Conductor.SongTimeAtRealtime`. Do not judge on `Update` frame time.
- Visuals are a pure function of song time (`NoteView`). No physics, no velocity.
- Visual offset moves the picture only. Judging uses real song time.
- Settings live in `GameSettings` / `KeyBindings` (PlayerPrefs). Read once per scene, do not poll per frame.
- Charts and songs load from `Resources`, input goes through Input System actions. Keeps the Switch port possible.
- Chart JSON: `{time_ms, lane, type, duration_ms}`. `duration_ms` > 0 = hold. Converter output must match what `ChartRecorder` writes.
- New game logic should come with a test in `Tools/LogicTests/` when it can be tested without Unity.

## Web (WebGL) rules

- No threads, no `System.IO` file access to real paths, no `Application.Quit`. Use `#if UNITY_WEBGL` guards (see `SongSelect`).
- File output goes through `WebDownload.SaveText` (browser download on web, file on desktop).
- Web is for testing flow and visuals. Timing feel is judged on a Windows desktop build, not the browser.
- `run.sh` compiles once with `-define:UNITY_WEBGL`, keep web-only code compiling there.
- Not verified on web: input event timestamp precision, Web Audio latency and `PlayScheduled` accuracy, `OnApplicationFocus` firing on tab hide, jslib download.

## Known risks (verify first in real Unity)

- Transcriber is tested with synthetic audio only. Real songs: expect half/double tempo picks and a rough melody.
  Constant tempo assumed. `ONSET_BIAS_SEC` in `rhythm.py` was measured on clicks; real drums may sit a few ms off.

- Scene builder untested in a real editor. Manual wiring steps are in README.
- Pause/resume audio lock (`AudioSource.Pause`) may drift a few ms.
- Tap sound latency depends on DSP buffer (set Best Latency). Test in a build, not the editor.
- Preview uses `Resources.LoadAsync`. A big library needs Addressables or streaming.
- Resources audio is memory heavy. Fine for a few songs, not for Switch.

## Ideas not built yet

Touch controls for web/mobile, real UI (UI Toolkit/uGUI) replacing IMGUI, particle effects, per-lane hit sounds, hold loop sound,
StepMania `.sm` import (beats to ms, needs BPM changes and stops), Steamworks build, Steam Deck test,
Switch audio via Addressables.
