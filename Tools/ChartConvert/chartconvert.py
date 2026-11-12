#!/usr/bin/env python3
"""
chartconvert: turn other chart formats into RhythmStarter chart JSON.

Input formats
  .osu   osu!mania beatmap (any key count, see --fold)
  .csv   plain text, one note per line:  time_ms,lane[,duration_ms]   (lane is 0-based, # starts a comment)

Output
  Chart JSON for Assets/Resources/Charts/<name>.json, same format the recorder writes.
  Optionally appends an entry to Assets/Resources/songs.json.

Examples
  python chartconvert.py song.osu -o ../../Assets/Resources/Charts/mysong.json
  python chartconvert.py song.osu -o out.json --fold            # 5K / 7K -> 4 lanes
  python chartconvert.py notes.csv -o out.json --title "My Song" --bpm 128
  python chartconvert.py song.osu -o ../../Assets/Resources/Charts/mysong.json \\
        --songs-json ../../Assets/Resources/songs.json --audio Audio/mysong --level 4

Only convert charts and audio you have the rights to use.
No dependencies. Python 3.8+.
"""
import argparse
import json
import os
import sys

TARGET_LANES = 4


class ConvertError(Exception):
    pass


# ---------------------------------------------------------------- parsing

def _sections(text):
    """Split an .osu file into {section name: [lines]}."""
    out, name = {}, None
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("//"):
            continue
        if line.startswith("[") and line.endswith("]"):
            name = line[1:-1]
            out[name] = []
        elif name is not None:
            out[name].append(line)
    return out


def _kv(lines):
    d = {}
    for line in lines:
        if ":" in line:
            k, v = line.split(":", 1)
            d[k.strip()] = v.strip()
    return d


def parse_osu(text):
    """osu!mania -> dict(title, artist, bpm, lanes, notes). Notes use time_ms/lane/duration_ms."""
    sec = _sections(text)
    general = _kv(sec.get("General", []))
    meta = _kv(sec.get("Metadata", []))
    diff = _kv(sec.get("Difficulty", []))

    if general.get("Mode") != "3":
        raise ConvertError("Not an osu!mania beatmap (General Mode must be 3).")
    if "HitObjects" not in sec:
        raise ConvertError("No [HitObjects] section.")

    try:
        keys = int(float(diff.get("CircleSize", "4")))
    except ValueError:
        raise ConvertError("Bad CircleSize (key count).")
    if keys < 1:
        raise ConvertError("Key count must be at least 1.")

    # First uninherited timing point with a positive beat length = base BPM.
    bpm = None
    for line in sec.get("TimingPoints", []):
        f = line.split(",")
        try:
            beat_len = float(f[1])
        except (IndexError, ValueError):
            continue
        uninherited = f[6].strip() == "1" if len(f) > 6 else beat_len > 0
        if uninherited and beat_len > 0:
            bpm = round(60000.0 / beat_len, 2)
            break

    notes = []
    for line in sec["HitObjects"]:
        f = line.split(",")
        if len(f) < 5:
            continue
        try:
            x, t, typ = int(float(f[0])), int(float(f[2])), int(f[3])
        except ValueError:
            continue
        lane = max(0, min(keys - 1, x * keys // 512))
        dur = 0
        if typ & 128:  # mania hold: endTime is first part of the 6th field
            try:
                dur = max(0, int(f[5].split(":")[0]) - t)
            except (IndexError, ValueError):
                dur = 0
        notes.append({"time_ms": t, "lane": lane, "duration_ms": dur})

    return {
        "title": meta.get("Title") or "Untitled",
        "artist": meta.get("Artist") or "",
        "bpm": bpm,
        "lanes": keys,
        "notes": notes,
    }


def parse_csv(text):
    """time_ms,lane[,duration_ms] per line. A non-numeric first line is treated as a header."""
    notes, lanes_seen = [], 0
    for n, raw in enumerate(text.splitlines(), 1):
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        parts = [p for p in line.replace(";", ",").replace("\t", ",").replace(" ", ",").split(",") if p != ""]
        try:
            t = int(float(parts[0]))
            lane = int(parts[1])
            dur = int(float(parts[2])) if len(parts) > 2 else 0
        except (IndexError, ValueError):
            if not notes and n <= 2:
                continue  # header row
            raise ConvertError(f"Line {n}: expected time_ms,lane[,duration_ms], got {raw!r}")
        if lane < 0:
            raise ConvertError(f"Line {n}: lane must be 0 or more.")
        notes.append({"time_ms": t, "lane": lane, "duration_ms": max(0, dur)})
        lanes_seen = max(lanes_seen, lane + 1)
    return {"title": None, "artist": "", "bpm": None, "lanes": max(lanes_seen, TARGET_LANES), "notes": notes}


# ---------------------------------------------------------------- shaping

def fold_lanes(notes, src_lanes, dst_lanes=TARGET_LANES):
    """Squash lanes into dst_lanes by proportion. clean() later drops notes that collide."""
    for n in notes:
        n["lane"] = min(dst_lanes - 1, n["lane"] * dst_lanes // src_lanes)
    return notes


def shorten_holds(notes, min_hold_ms):
    """Holds shorter than min_hold_ms are not worth holding: make them taps."""
    for n in notes:
        if 0 < n["duration_ms"] < min_hold_ms:
            n["duration_ms"] = 0
    return notes


def clean(notes, min_gap_ms=30, hold_gap_ms=100):
    """
    Sort, then drop notes that cannot be played: same lane closer than min_gap_ms to the previous note,
    or closer than hold_gap_ms after a hold ends (the player needs time to let go and press again).
    Returns (kept, dropped_count).
    """
    notes = sorted(notes, key=lambda n: (n["time_ms"], n["lane"]))
    kept, dropped = [], 0
    last = {}  # lane -> (end_time, was_hold)
    for n in notes:
        lane, t = n["lane"], n["time_ms"]
        if lane in last:
            end, was_hold = last[lane]
            gap = hold_gap_ms if was_hold else min_gap_ms
            if t < end + gap:
                dropped += 1
                continue
        kept.append(n)
        last[lane] = (t + n["duration_ms"], n["duration_ms"] > 0)
    return kept, dropped


# ---------------------------------------------------------------- output

def to_chart_json(title, lanes, notes):
    rows = []
    for n in notes:
        if n["duration_ms"] > 0:
            rows.append('{ "time_ms": %d, "lane": %d, "type": "hold", "duration_ms": %d }'
                        % (n["time_ms"], n["lane"], n["duration_ms"]))
        else:
            rows.append('{ "time_ms": %d, "lane": %d, "type": "tap" }' % (n["time_ms"], n["lane"]))
    body = ",\n    ".join(rows)
    return '{\n  "title": %s,\n  "lanes": %d,\n  "notes": [\n    %s\n  ]\n}\n' % (json.dumps(title), lanes, body)


def append_song(songs_path, entry):
    """Add entry to songs.json. Returns True if added, False if the id already exists."""
    data = {"songs": []}
    if os.path.exists(songs_path):
        with open(songs_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        data.setdefault("songs", [])
    if any(s.get("id") == entry["id"] for s in data["songs"]):
        return False
    data["songs"].append(entry)
    with open(songs_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
        f.write("\n")
    return True


# ---------------------------------------------------------------- main

def convert(text, fmt, fold=False, min_hold_ms=150, min_gap_ms=30, hold_gap_ms=100):
    """Returns (parsed_info, kept_notes, dropped_count). Raises ConvertError."""
    info = parse_osu(text) if fmt == "osu" else parse_csv(text)
    notes = info["notes"]
    if not notes:
        raise ConvertError("No notes found in input.")

    src = info["lanes"]
    if src != TARGET_LANES:
        if not fold:
            raise ConvertError(
                f"Source has {src} lanes, the game has {TARGET_LANES}. "
                f"Pass --fold to squash lanes (colliding notes get dropped).")
        if src > TARGET_LANES:
            fold_lanes(notes, src)
        else:
            raise ConvertError(f"Source has {src} lanes, fewer than {TARGET_LANES}. Cannot fold up.")

    shorten_holds(notes, min_hold_ms)
    kept, dropped = clean(notes, min_gap_ms, hold_gap_ms)
    info["lanes"] = TARGET_LANES
    return info, kept, dropped


def main(argv=None):
    ap = argparse.ArgumentParser(description="Convert osu!mania or CSV charts to RhythmStarter JSON.")
    ap.add_argument("input")
    ap.add_argument("-o", "--output", help="output .json (default: next to input)")
    ap.add_argument("--format", choices=["osu", "csv"], help="force input format (default: by extension)")
    ap.add_argument("--fold", action="store_true", help="squash 5K/6K/7K... charts into 4 lanes")
    ap.add_argument("--offset-ms", type=int, default=0, help="shift every note by this many ms (can be negative)")
    ap.add_argument("--min-hold-ms", type=int, default=150, help="holds shorter than this become taps (default 150)")
    ap.add_argument("--min-gap-ms", type=int, default=30, help="min gap between notes in one lane (default 30)")
    ap.add_argument("--hold-gap-ms", type=int, default=100, help="min gap after a hold ends (default 100)")
    ap.add_argument("--title", help="song title (default: from file or output name)")
    ap.add_argument("--artist", help="artist for songs.json")
    ap.add_argument("--bpm", type=float, help="BPM for songs.json (default: from file)")
    ap.add_argument("--level", type=int, default=1, help="difficulty level for songs.json")
    ap.add_argument("--audio", help="Resources audio path for songs.json, e.g. Audio/mysong")
    ap.add_argument("--preview-start", type=float, default=0.0,
                    help="seconds into the audio where the song select preview starts (0 = automatic)")
    ap.add_argument("--songs-json", help="path to Assets/Resources/songs.json, entry is appended")
    a = ap.parse_args(argv)

    fmt = a.format or {".osu": "osu", ".csv": "csv"}.get(os.path.splitext(a.input)[1].lower())
    if fmt is None:
        print("error: cannot tell the format. Use .osu / .csv or pass --format.", file=sys.stderr)
        return 2

    try:
        with open(a.input, "r", encoding="utf-8-sig") as f:
            text = f.read()
        info, notes, dropped = convert(text, fmt, a.fold, a.min_hold_ms, a.min_gap_ms, a.hold_gap_ms)
    except (OSError, ConvertError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 1

    if a.offset_ms:
        for n in notes:
            n["time_ms"] += a.offset_ms
        notes = [n for n in notes if n["time_ms"] >= 0]

    stem = os.path.splitext(os.path.basename(a.output or a.input))[0]
    out_path = a.output or os.path.splitext(a.input)[0] + ".json"
    title = a.title or info["title"] or stem

    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(to_chart_json(title, TARGET_LANES, notes))

    holds = sum(1 for n in notes if n["duration_ms"] > 0)
    print(f"wrote {out_path}")
    print(f"  {len(notes)} notes ({len(notes) - holds} taps, {holds} holds), "
          f"{notes[0]['time_ms'] / 1000:.2f}s to {(notes[-1]['time_ms'] + notes[-1]['duration_ms']) / 1000:.2f}s")
    if dropped:
        print(f"  dropped {dropped} unplayable notes (too close in one lane)")
    bpm = a.bpm or info["bpm"]
    if bpm:
        print(f"  BPM {bpm:g}")

    if a.songs_json:
        entry = {
            "id": stem,
            "title": title,
            "artist": a.artist or info["artist"] or "",
            "bpm": bpm or 120,
            "audio": a.audio or f"Audio/{stem}",
            "chart": stem,
            "level": a.level,
            "previewStart": a.preview_start,
        }
        if append_song(a.songs_json, entry):
            print(f"  added '{stem}' to {a.songs_json}")
        else:
            print(f"  songs.json already has id '{stem}', left unchanged")
    return 0


if __name__ == "__main__":
    sys.exit(main())
