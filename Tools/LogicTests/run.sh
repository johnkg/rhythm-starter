#!/usr/bin/env bash
# Logic tests for Judge, Conductor clock and pause. No Unity needed, just Mono (mcs + mono).
#   Ubuntu/Debian: sudo apt install mono-mcs mono-runtime
#   macOS:         brew install mono
# Unity API is faked in UnityStubs.cs / EditorStubs.cs. Tests poke private methods by reflection.
# Run from anywhere:  bash Tools/LogicTests/run.sh
set -e
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$(mktemp -d)"
S="$ROOT/Assets/Scripts"
T="$ROOT/Tools/LogicTests"
CORE="$S/Judge.cs $S/NoteSpawner.cs $S/NoteView.cs $S/ChartData.cs $S/Conductor.cs $S/AudioTimeline.cs $S/SmoothClock.cs $S/GameSettings.cs $S/KeyBindings.cs"

echo "== compile every runtime script against stubs (catches typos and type errors)"
mcs -target:library -warn:2 -out:"$OUT/all.dll" "$T/UnityStubs.cs" "$S"/*.cs

echo "== compile with UNITY_WEBGL defined (web-only code paths)"
mcs -target:library -warn:2 -define:UNITY_WEBGL -out:"$OUT/web.dll" "$T/UnityStubs.cs" "$S"/*.cs

echo "== compile editor setup script (both Input System branches)"
mcs -target:library -warn:2 -define:ENABLE_INPUT_SYSTEM -out:"$OUT/editor1.dll" "$T/UnityStubs.cs" "$T/EditorStubs.cs" "$S"/*.cs "$ROOT/Assets/Editor"/*.cs
mcs -target:library -out:"$OUT/editor2.dll" "$T/UnityStubs.cs" "$T/EditorStubs.cs" "$S"/*.cs "$ROOT/Assets/Editor"/*.cs

echo "== Judge tests"
mcs -out:"$OUT/judge.exe" "$T/UnityStubs.cs" "$T/JudgeTests.cs" $CORE
mono "$OUT/judge.exe"

echo "== Clock and pause tests"
mcs -out:"$OUT/clock.exe" "$T/UnityStubs.cs" "$T/ClockAndPauseTests.cs" $CORE
mono "$OUT/clock.exe"

echo "== Key binding tests"
mcs -out:"$OUT/keys.exe" "$T/UnityStubs.cs" "$T/KeyBindingTests.cs" $S/KeyBindings.cs $S/GameSettings.cs
mono "$OUT/keys.exe"

echo "== Preview tests"
mcs -out:"$OUT/preview.exe" "$T/UnityStubs.cs" "$T/PreviewTests.cs" $S/PreviewPlayer.cs $S/SongInfo.cs $S/GameSettings.cs
mono "$OUT/preview.exe"

echo "== Tap sound tests"
mcs -out:"$OUT/tap.exe" "$T/UnityStubs.cs" "$T/TapSoundTests.cs" $S/TapSound.cs $CORE
mono "$OUT/tap.exe"

echo "== Focus loss pause tests"
mcs -out:"$OUT/focus.exe" "$T/UnityStubs.cs" "$T/FocusTests.cs" $S/PauseMenu.cs $S/ResultsScreen.cs $S/SongInfo.cs $CORE
mono "$OUT/focus.exe"

echo "== Smooth clock tests"
mcs -out:"$OUT/smooth.exe" "$T/SmoothClockTests.cs" "$S/SmoothClock.cs"
mono "$OUT/smooth.exe"

echo "== Audio timeline tests"
mcs -out:"$OUT/timeline.exe" "$T/UnityStubs.cs" "$T/TimelineTests.cs" "$S/AudioTimeline.cs" "$S/SmoothClock.cs"
mono "$OUT/timeline.exe"
