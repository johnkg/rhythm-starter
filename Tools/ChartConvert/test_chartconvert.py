import json
import os
import tempfile
import unittest

import chartconvert as cc

OSU_4K = """osu file format v14

[General]
Mode: 3

[Metadata]
Title:Example Song
Artist:Someone

[Difficulty]
CircleSize:4

[TimingPoints]
0,500,4,1,0,100,1,0
1000,-100,4,1,0,100,0,0

[HitObjects]
64,192,2000,1,0,0:0:0:0:
192,192,2500,1,0,0:0:0:0:
320,192,3000,128,0,4000:0:0:0:0:
448,192,3500,1,0,0:0:0:0:
"""


class OsuTests(unittest.TestCase):
    def test_4k_basic(self):
        info, notes, dropped = cc.convert(OSU_4K, "osu")
        self.assertEqual(info["title"], "Example Song")
        self.assertEqual(info["artist"], "Someone")
        self.assertEqual(info["bpm"], 120.0)          # 60000 / 500, inherited point ignored
        self.assertEqual(dropped, 0)
        self.assertEqual([(n["time_ms"], n["lane"], n["duration_ms"]) for n in notes],
                         [(2000, 0, 0), (2500, 1, 0), (3000, 2, 1000), (3500, 3, 0)])

    def test_not_mania(self):
        with self.assertRaises(cc.ConvertError):
            cc.convert(OSU_4K.replace("Mode: 3", "Mode: 0"), "osu")

    def test_seven_key_needs_fold(self):
        seven = OSU_4K.replace("CircleSize:4", "CircleSize:7")
        with self.assertRaises(cc.ConvertError):
            cc.convert(seven, "osu")
        info, notes, _ = cc.convert(seven, "osu", fold=True)
        self.assertEqual(info["lanes"], 4)
        self.assertTrue(all(0 <= n["lane"] < 4 for n in notes))

    def test_fold_drops_collisions(self):
        # 8K: lanes 0 and 1 both fold to lane 0, same time -> one is dropped
        text = OSU_4K.split("[HitObjects]")[0].replace("CircleSize:4", "CircleSize:8") + \
               "[HitObjects]\n0,192,2000,1,0,0:0:0:0:\n64,192,2000,1,0,0:0:0:0:\n"
        _, notes, dropped = cc.convert(text, "osu", fold=True)
        self.assertEqual(len(notes), 1)
        self.assertEqual(dropped, 1)

    def test_short_hold_becomes_tap(self):
        text = OSU_4K.replace("4000:0:0:0:0:", "3100:0:0:0:0:")   # 100 ms hold
        _, notes, _ = cc.convert(text, "osu", min_hold_ms=150)
        self.assertEqual(notes[2]["duration_ms"], 0)

    def test_no_notes(self):
        with self.assertRaises(cc.ConvertError):
            cc.convert(OSU_4K.split("[HitObjects]")[0] + "[HitObjects]\n", "osu")


class CsvTests(unittest.TestCase):
    def test_header_comments_blank(self):
        text = "# comment\ntime_ms,lane,duration_ms\n\n2000,0\n2500,1  # inline\n3000,2,1000\n"
        _, notes, dropped = cc.convert(text, "csv")
        self.assertEqual([(n["time_ms"], n["lane"], n["duration_ms"]) for n in notes],
                         [(2000, 0, 0), (2500, 1, 0), (3000, 2, 1000)])
        self.assertEqual(dropped, 0)

    def test_bad_line(self):
        with self.assertRaises(cc.ConvertError):
            cc.convert("2000,0\nabc,def\n", "csv")

    def test_too_many_lanes_needs_fold(self):
        with self.assertRaises(cc.ConvertError):
            cc.convert("2000,5\n", "csv")


class CleanTests(unittest.TestCase):
    def test_gap_after_hold(self):
        notes = [{"time_ms": 1000, "lane": 0, "duration_ms": 500},
                 {"time_ms": 1550, "lane": 0, "duration_ms": 0},     # 50 ms after hold end: too tight
                 {"time_ms": 1700, "lane": 0, "duration_ms": 0}]     # 200 ms after: ok
        kept, dropped = cc.clean(notes)
        self.assertEqual([n["time_ms"] for n in kept], [1000, 1700])
        self.assertEqual(dropped, 1)

    def test_other_lane_unaffected(self):
        notes = [{"time_ms": 1000, "lane": 0, "duration_ms": 500},
                 {"time_ms": 1100, "lane": 1, "duration_ms": 0}]
        kept, dropped = cc.clean(notes)
        self.assertEqual(len(kept), 2)


class OutputTests(unittest.TestCase):
    def test_json_matches_game_format(self):
        _, notes, _ = cc.convert(OSU_4K, "osu")
        data = json.loads(cc.to_chart_json("Example Song", 4, notes))
        self.assertEqual(data["title"], "Example Song")
        self.assertEqual(data["lanes"], 4)
        self.assertEqual(data["notes"][0], {"time_ms": 2000, "lane": 0, "type": "tap"})
        self.assertEqual(data["notes"][2], {"time_ms": 3000, "lane": 2, "type": "hold", "duration_ms": 1000})

    def test_songs_json_append_and_dedupe(self):
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "songs.json")
            entry = {"id": "a", "title": "A", "artist": "", "bpm": 120, "audio": "Audio/a", "chart": "a", "level": 1}
            self.assertTrue(cc.append_song(p, entry))          # creates file
            self.assertFalse(cc.append_song(p, entry))         # duplicate id
            cc.append_song(p, dict(entry, id="b"))
            with open(p) as f:
                self.assertEqual([s["id"] for s in json.load(f)["songs"]], ["a", "b"])

    def test_cli_end_to_end(self):
        here = os.path.dirname(os.path.abspath(__file__))
        with tempfile.TemporaryDirectory() as d:
            out = os.path.join(d, "ex.json")
            songs = os.path.join(d, "songs.json")
            rc = cc.main([os.path.join(here, "samples", "example.osu"), "-o", out,
                          "--songs-json", songs, "--level", "3", "--preview-start", "12.5"])
            self.assertEqual(rc, 0)
            with open(out) as f:
                chart = json.load(f)
            self.assertEqual(len(chart["notes"]), 6)
            with open(songs) as f:
                s = json.load(f)["songs"][0]
            self.assertEqual((s["id"], s["bpm"], s["level"], s["audio"]), ("ex", 120.0, 3, "Audio/ex"))
            self.assertEqual(s["previewStart"], 12.5)
            rc = cc.main([os.path.join(here, "samples", "example.csv"), "-o", os.path.join(d, "c.json")])
            self.assertEqual(rc, 0)

    def test_offset(self):
        here = os.path.dirname(os.path.abspath(__file__))
        with tempfile.TemporaryDirectory() as d:
            out = os.path.join(d, "o.json")
            cc.main([os.path.join(here, "samples", "example.csv"), "-o", out, "--offset-ms", "-100"])
            with open(out) as f:
                self.assertEqual(json.load(f)["notes"][0]["time_ms"], 1900)


if __name__ == "__main__":
    unittest.main()
