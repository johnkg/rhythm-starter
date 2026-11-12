#!/usr/bin/env python3
"""
transcribe: listen to a song file and write a playable RhythmStarter chart, plus optional PDFs.

  python transcribe.py song.mp3                      chart JSON next to the song
  python transcribe.py song.mp3 --pdf                + chart PDF + melody sheet PDF + melody MIDI
  python transcribe.py song.mp3 --pdf --project ../..    also adds the song to the game (audio, chart, songs.json)

Outputs (named after the song id)
  <id>.json          chart, same format the game and the recorder use
  <id>_chart.pdf     the chart on paper: 4 lanes, bottom to top           (--pdf or --chart-pdf)
  <id>_melody.pdf    main melody as simple sheet music, a draft           (--pdf or --sheet-pdf)
  <id>_melody.mid    the same melody as MIDI, opens in MuseScore          (with the sheet, or --midi)

What it can and cannot do
  Chart: finds the beat and the strong hits, steady tempo assumed. Good first draft, play it and fix it.
  Melody: one line, no chords, no machine learning. Wrong octaves and missed notes are normal on full mixes.

Needs: Python 3.8+, numpy, reportlab (for PDFs), ffmpeg on PATH (for anything that is not a .wav).
Only use songs you have the rights to use.
"""
import argparse
import os
import re
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "ChartConvert"))

import audio            # noqa: E402
import rhythm           # noqa: E402
import melody           # noqa: E402
import midi             # noqa: E402
import chartconvert     # noqa: E402

_NOTE_RE = re.compile(r"^([A-Ga-g])([#b]?)(-?\d)$")


def parse_pitch(text):
    """'C4' or '60' -> MIDI number."""
    m = _NOTE_RE.match(text.strip())
    if m:
        base = {"c": 0, "d": 2, "e": 4, "f": 5, "g": 7, "a": 9, "b": 11}[m.group(1).lower()]
        acc = {"#": 1, "b": -1, "": 0}[m.group(2)]
        return (int(m.group(3)) + 1) * 12 + base + acc
    return int(text)


def slug(name):
    s = re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")
    return s or "song"


def run(a, log=print):
    """Does the work. Returns a dict of what was made. Raises AudioError / ValueError with a readable message."""
    src = a.input
    base = os.path.splitext(os.path.basename(src))[0]
    song_id = a.id or slug(base)
    title = a.title or base.replace("_", " ").strip()
    out_dir = a.out or os.path.dirname(os.path.abspath(src))
    os.makedirs(out_dir, exist_ok=True)
    result = {"id": song_id, "files": []}

    # Game audio first: the chart is then timed against the very file the game plays.
    analyse_path = src
    audio_res = None
    if a.project:
        res = os.path.join(a.project, "Assets", "Resources")
        if not os.path.isdir(res):
            raise ValueError("Not a RhythmStarter project (no Assets/Resources): " + a.project)
        os.makedirs(os.path.join(res, "Audio"), exist_ok=True)
        os.makedirs(os.path.join(res, "Charts"), exist_ok=True)
        ext = os.path.splitext(src)[1].lower()
        if a.audio_format == "ogg" and ext != ".ogg" and audio.have_ffmpeg():
            dst = os.path.join(res, "Audio", song_id + ".ogg")
            audio.convert_audio(src, dst)
        else:
            dst = os.path.join(res, "Audio", song_id + ext)
            if os.path.abspath(dst) != os.path.abspath(src):
                shutil.copyfile(src, dst)
        analyse_path = dst
        audio_res = "Audio/" + song_id
        result["files"].append(dst)

    log("Reading audio...")
    y = audio.load_audio(analyse_path)
    an = rhythm.analyse(y, audio.SR)
    log("Finding the beat...")
    tempo = rhythm.find_tempo(an, bpm=a.bpm, offset=None if a.offset_ms is None else a.offset_ms / 1000.0,
                              beats_per_bar=a.beats_per_bar)
    snap = tempo.steady if a.snap is None else a.snap
    log("  %.2f BPM, first beat at %d ms, %d%% of strong hits on the beat grid%s" % (
        tempo.bpm, round(tempo.offset * 1000), round(tempo.fit * 100),
        "" if tempo.steady else "  (tempo not steady or not found)"))
    if a.bpm is None:
        log("  Feels twice too fast or too slow? Pass the tempo yourself, for example --bpm %.0f" % (
            tempo.bpm * 2 if tempo.bpm < 100 else tempo.bpm / 2))
    if not tempo.steady and a.snap is None:
        log("  Notes are NOT snapped to the beat. If you know the tempo, pass --bpm (and --offset-ms).")

    notes = rhythm.make_chart(an, tempo, level=a.level, snap=snap, holds=not a.no_holds)
    notes = chartconvert.shorten_holds(notes, 150)
    notes, dropped = chartconvert.clean(notes)
    if not notes:
        raise ValueError("No notes found. Is the file silent or very quiet?")
    holds = sum(1 for n in notes if n["duration_ms"] > 0)
    log("Chart: %d notes (%d holds), level %d, %.1f notes per second" % (len(notes), holds, a.level, len(notes) / an.duration))

    chart_text = chartconvert.to_chart_json(title, 4, notes)
    chart_path = os.path.join(out_dir, song_id + ".json")
    with open(chart_path, "w", encoding="utf-8") as f:
        f.write(chart_text)
    result["files"].append(chart_path)
    result.update(bpm=tempo.bpm, offset=tempo.offset, notes=notes, snapped=snap, steady=tempo.steady, duration=an.duration)

    want_chart_pdf = a.pdf or a.chart_pdf
    want_sheet = a.pdf or a.sheet_pdf
    if want_chart_pdf:
        import chart_pdf
        p = os.path.join(out_dir, song_id + "_chart.pdf")
        pages = chart_pdf.write_chart_pdf(p, notes, tempo, an.duration, title, a.artist or "", a.level,
                                          beats_per_bar=a.beats_per_bar, snapped=snap, source=os.path.basename(src))
        log("Chart PDF: %d page(s)" % pages)
        result["files"].append(p)

    if want_sheet or a.midi:
        log("Following the melody (slow part)...")
        lo, hi = parse_pitch(a.melody_low), parse_pitch(a.melody_high)
        if hi - lo < 12:
            raise ValueError("--melody-high must be at least an octave above --melody-low.")
        pitch, strength, fr = melody.pitch_track(y, audio.SR, lo, hi)
        mnotes, slots_per_bar = melody.to_notes(pitch, strength, fr, tempo, grid=a.sheet_grid, beats_per_bar=a.beats_per_bar)
        key_name, flats = melody.guess_key(mnotes)
        log("Melody: %d notes, key guess %s" % (len(mnotes), key_name))
        result.update(melody=mnotes, key=key_name)
        mid_path = os.path.join(out_dir, song_id + "_melody.mid")
        midi.write_midi(mid_path, mnotes, tempo.bpm, grid=a.sheet_grid, beats_per_bar=a.beats_per_bar, title=title)
        result["files"].append(mid_path)
        if want_sheet:
            import sheet_pdf
            p = os.path.join(out_dir, song_id + "_melody.pdf")
            warn = "" if tempo.steady else "Tempo was not steady: note lengths and bar lines are unreliable."
            pages = sheet_pdf.write_sheet_pdf(p, mnotes, slots_per_bar, tempo.bpm, title, a.artist or "", key_name, flats,
                                              beats_per_bar=a.beats_per_bar, source=os.path.basename(src), note=warn)
            log("Melody PDF: %d page(s)" % pages)
            result["files"].append(p)

    if a.project:
        res = os.path.join(a.project, "Assets", "Resources")
        game_chart = os.path.join(res, "Charts", song_id + ".json")
        with open(game_chart, "w", encoding="utf-8") as f:
            f.write(chart_text)
        result["files"].append(game_chart)
        entry = {"id": song_id, "title": title, "artist": a.artist or "", "bpm": round(tempo.bpm, 2),
                 "audio": audio_res, "chart": song_id, "level": a.level,
                 "previewStart": rhythm.preview_start(an, tempo)}
        added = chartconvert.append_song(os.path.join(res, "songs.json"), entry)
        result["added"] = added
        log("Added to the game: songs.json entry '%s'." % song_id if added else
            "songs.json already has id '%s': entry left as it was, chart and audio were replaced." % song_id)
    return result


def build_parser():
    ap = argparse.ArgumentParser(description="Turn a song file into a RhythmStarter chart, with optional PDFs.")
    ap.add_argument("input", help="song file: mp3, ogg, wav, m4a, flac...")
    ap.add_argument("--out", help="folder for the outputs (default: next to the song)")
    ap.add_argument("--id", help="song id, used for file names and songs.json (default: from the file name)")
    ap.add_argument("--title")
    ap.add_argument("--artist")
    ap.add_argument("--level", type=int, default=3, help="difficulty 1 to 10: more notes, faster rhythms (default 3)")
    ap.add_argument("--bpm", type=float, help="tempo if you know it (skips tempo detection)")
    ap.add_argument("--offset-ms", type=float, help="time of a beat in ms if you know it")
    ap.add_argument("--beats-per-bar", type=int, default=4)
    ap.add_argument("--snap", dest="snap", action="store_true", default=None, help="always snap notes to the beat grid")
    ap.add_argument("--no-snap", dest="snap", action="store_false", help="never snap, keep the detected times")
    ap.add_argument("--no-holds", action="store_true", help="tap notes only")
    ap.add_argument("--pdf", action="store_true", help="write both PDFs (chart and melody sheet)")
    ap.add_argument("--chart-pdf", action="store_true", help="write the chart PDF only")
    ap.add_argument("--sheet-pdf", action="store_true", help="write the melody sheet PDF only")
    ap.add_argument("--midi", action="store_true", help="write the melody MIDI without the sheet PDF")
    ap.add_argument("--sheet-grid", type=int, choices=[8, 16], default=8,
                    help="shortest note in the sheet: 8 = eighth notes (cleaner, default), 16 = sixteenths")
    ap.add_argument("--melody-low", default="E3", help="lowest melody note to look for (default E3)")
    ap.add_argument("--melody-high", default="E6", help="highest melody note to look for (default E6)")
    ap.add_argument("--project", help="RhythmStarter project folder: copy audio, chart and add to songs.json")
    ap.add_argument("--audio-format", choices=["ogg", "keep"], default="ogg",
                    help="with --project: convert the audio to ogg (default, same timing on every platform) or keep the file")
    return ap


def main(argv=None):
    a = build_parser().parse_args(argv)
    if not 1 <= a.level <= 10:
        print("error: --level must be 1 to 10.", file=sys.stderr)
        return 2
    try:
        r = run(a)
    except (audio.AudioError, ValueError) as e:
        print("error: " + str(e), file=sys.stderr)
        return 1
    print("Wrote:")
    for f in r["files"]:
        print("  " + f)
    return 0


if __name__ == "__main__":
    sys.exit(main())
