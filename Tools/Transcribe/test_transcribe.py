"""Tests for the transcriber. Synthetic audio with known tempo and melody, no real songs.
Run:  python3 -m unittest test_transcribe      (needs numpy, scipy; reportlab for PDF tests; ffmpeg for mp3 tests)
"""
import json
import os
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import audio
import melody
import midi
import rhythm
import synth
import transcribe

try:
    import reportlab  # noqa: F401
    HAVE_PDF = True
except ImportError:
    HAVE_PDF = False


def _has_encoder(name):
    if not audio.have_ffmpeg():
        return False
    out = subprocess.run(["ffmpeg", "-v", "error", "-encoders"], capture_output=True).stdout.decode(errors="replace")
    return name in out


def _json(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def _wrap(err, period):
    return (err + period / 2) % period - period / 2


class TempoTests(unittest.TestCase):
    def test_click_tracks(self):
        for bpm, off in [(120, 0.5), (97.3, 0.31), (151.7, 0.2), (80, 0.6)]:
            y, _ = synth.click_track(bpm, 60, off)
            t = rhythm.find_tempo(rhythm.analyse(y, synth.SR))
            self.assertAlmostEqual(t.bpm, bpm, delta=0.02, msg="bpm %s" % bpm)
            self.assertLess(abs(_wrap(t.offset - off, t.beat)) * 1000, 6.0, "beat position at %s bpm" % bpm)
            self.assertTrue(t.steady)

    def test_drift_over_a_long_song(self):
        # 4 minutes: the last beat must still be on time
        y, times = synth.click_track(128, 240, 0.4)
        t = rhythm.find_tempo(rhythm.analyse(y, synth.SR))
        k = round((times[-1] - t.offset) / t.beat)
        self.assertLess(abs(t.offset + k * t.beat - times[-1]) * 1000, 10.0)

    def test_fast_song_may_read_as_half_speed_but_stays_on_the_grid(self):
        y, _ = synth.click_track(174, 40, 0.12)
        t = rhythm.find_tempo(rhythm.analyse(y, synth.SR))
        self.assertTrue(abs(t.bpm - 174) < 0.1 or abs(t.bpm - 87) < 0.05, t.bpm)

    def test_given_bpm_is_used_as_is(self):
        y, _ = synth.click_track(174, 40, 0.12)
        t = rhythm.find_tempo(rhythm.analyse(y, synth.SR), bpm=174.0)
        self.assertEqual(t.bpm, 174.0)
        self.assertLess(abs(_wrap(t.offset - 0.12, t.beat)) * 1000, 6.0)

    def test_random_hits_are_not_called_steady(self):
        rng = np.random.default_rng(5)
        y = np.zeros(40 * synth.SR, np.float32)
        click, _ = synth.click_track(600, 0.5, 0.0)
        for t in np.sort(rng.uniform(0.5, 39, 90)):
            a = int(t * synth.SR)
            y[a:a + 400] += click[:400]
        an = rhythm.analyse(y, synth.SR)
        t = rhythm.find_tempo(an)
        self.assertFalse(t.steady)
        notes = rhythm.make_chart(an, t, level=4)          # falls back to raw hit times
        self.assertGreater(len(notes), 30)

    def test_bar_origin(self):
        t = rhythm.Tempo(120.0, 0.3, bar_beat=0)
        self.assertAlmostEqual(t.bar_origin(4), 0.3)
        t = rhythm.Tempo(120.0, 0.3, bar_beat=3)            # 1.5 s of pickup before the first downbeat
        self.assertAlmostEqual(t.bar_origin(4), 1.8 - 2.0)


class ChartTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.bpm = 120.0
        cls.y, cls.mel, cls.dur = synth.song(bpm=cls.bpm, bars=24, offset=0.4)
        cls.an = rhythm.analyse(cls.y, synth.SR)
        cls.tempo = rhythm.find_tempo(cls.an)

    def test_tempo_of_full_mix(self):
        self.assertAlmostEqual(self.tempo.bpm, 120.0, delta=0.05)
        self.assertLess(abs(_wrap(self.tempo.offset - 0.4, 0.5)) * 1000, 10.0)
        self.assertEqual(self.tempo.bar_beat, 0)             # kick on 1 found as the bar start

    def test_notes_sit_on_the_grid(self):
        notes = rhythm.make_chart(self.an, self.tempo, level=5)
        self.assertGreater(len(notes), 50)
        eighth = 0.25
        worst = max(abs(_wrap(n["time_ms"] / 1000.0 - 0.4, eighth)) for n in notes)
        self.assertLess(worst * 1000, 12.0)

    def test_all_lanes_used_and_valid(self):
        notes = rhythm.make_chart(self.an, self.tempo, level=5)
        lanes = np.bincount([n["lane"] for n in notes], minlength=4)
        self.assertEqual(len(lanes), 4)
        self.assertTrue((lanes >= len(notes) * 0.1).all(), lanes)
        self.assertEqual(notes, sorted(notes, key=lambda n: (n["time_ms"], n["lane"])))

    def test_higher_level_has_more_notes(self):
        counts = [len(rhythm.make_chart(self.an, self.tempo, level=l)) for l in (1, 3, 6, 9)]
        self.assertEqual(counts, sorted(counts))
        self.assertGreater(counts[-1], counts[0] * 1.5)

    def test_level_1_is_on_beats_only(self):
        notes = rhythm.make_chart(self.an, self.tempo, level=1)
        self.assertTrue(notes)
        for n in notes:
            self.assertLess(abs(_wrap(n["time_ms"] / 1000.0 - 0.4, 0.5)) * 1000, 12.0)

    def test_no_fast_repeats_in_one_lane(self):
        import chartconvert
        notes = rhythm.make_chart(self.an, self.tempo, level=9)
        kept, dropped = chartconvert.clean(notes)
        self.assertLess(dropped, len(notes) * 0.05)

    def test_holds_on_long_notes(self):
        beat = 0.5
        y = np.zeros(int(34 * synth.SR), np.float32)
        for bar in range(16):
            synth._add(y, 0.5 + bar * 4 * beat, synth._tone(synth.midi_hz(64), 3 * beat), 0.5)
        an = rhythm.analyse(y, synth.SR)
        t = rhythm.find_tempo(an, bpm=120.0)
        notes = rhythm.make_chart(an, t, level=3)
        holds = [n for n in notes if n["duration_ms"] > 0]
        self.assertGreaterEqual(len(holds), 12)
        for h in holds:
            self.assertGreaterEqual(h["duration_ms"], 500)
            self.assertLessEqual(h["duration_ms"], 1500)        # the tone lasts 1.5 s
        self.assertFalse([n for n in rhythm.make_chart(an, t, level=3, holds=False) if n["duration_ms"] > 0])

    def test_preview_start_is_inside_the_song(self):
        p = rhythm.preview_start(self.an, self.tempo)
        self.assertGreaterEqual(p, 0.0)
        self.assertLess(p, self.dur)


class MelodyTests(unittest.TestCase):
    def _score(self, bpm, grid):
        y, mel, _ = synth.song(bpm=bpm, bars=24, offset=0.4)
        t = rhythm.find_tempo(rhythm.analyse(y, synth.SR))
        p, s, fr = melody.pitch_track(y, synth.SR)
        notes, spb = melody.to_notes(p, s, fr, t, grid=grid)
        per = grid // 4
        origin, slot = t.bar_origin(4), t.beat / per
        truth, got = {}, {}
        for b, L, m in mel:
            for k in range(int(round(L * per))):
                truth[int(round((0.4 + b * 60.0 / bpm - origin) / slot)) + k] = m
        for n in notes:
            for k in range(n["length"]):
                got[n["slot"] + k] = n["midi"]
        keys = set(truth) | set(got)
        return sum(1 for k in keys if truth.get(k) == got.get(k)) / len(keys), notes, spb

    def test_melody_over_a_band(self):
        for bpm, grid in ((120, 8), (96, 8), (140, 16)):
            acc, notes, spb = self._score(bpm, grid)
            self.assertGreater(acc, 0.95, "bpm %s grid %s" % (bpm, grid))
            self.assertEqual(spb, grid)
            self.assertEqual(melody.guess_key(notes)[0], "C major")

    def test_silence_gives_no_notes(self):
        y = (np.random.default_rng(1).standard_normal(8 * synth.SR) * 1e-4).astype(np.float32)
        y[:100] = 0.5                                         # one click so the file is not empty
        p, s, fr = melody.pitch_track(y, synth.SR)
        notes, _ = melody.to_notes(p, s, fr, rhythm.Tempo(120.0, 0.0), grid=8)
        self.assertLess(len(notes), 3)

    def test_key_guess_and_names(self):
        g_major = [{"midi": m, "length": 2, "slot": 0} for m in (67, 69, 71, 72, 74, 76, 78, 79, 67, 74)]
        self.assertEqual(melody.guess_key(g_major), ("G major", False))
        f_major = [{"midi": m, "length": 2, "slot": 0} for m in (65, 67, 69, 70, 72, 74, 76, 77, 65, 72)]
        self.assertEqual(melody.guess_key(f_major), ("F major", True))
        self.assertEqual(melody.note_name(61), "C#4")
        self.assertEqual(melody.note_name(61, flats=True), "Db4")
        self.assertEqual(transcribe.parse_pitch("C4"), 60)
        self.assertEqual(transcribe.parse_pitch("Bb3"), 58)
        self.assertEqual(transcribe.parse_pitch("64"), 64)


@unittest.skipUnless(HAVE_PDF, "reportlab not installed")
class SheetTests(unittest.TestCase):
    def test_split_values(self):
        import sheet_pdf as sp
        self.assertEqual(sp.split_values(0, 16, 4), [(0, 16, False)])                    # whole note
        self.assertEqual(sp.split_values(0, 12, 4), [(0, 12, True)])                     # dotted half
        self.assertEqual(sp.split_values(0, 6, 4), [(0, 6, True)])                       # dotted quarter
        self.assertEqual(sp.split_values(4, 3, 4), [(4, 3, True)])                       # dotted eighth
        self.assertEqual(sp.split_values(1, 3, 4), [(1, 1, False), (2, 2, False)])       # off the beat: split
        self.assertEqual(sp.split_values(0, 5, 2), [(0, 4, False), (4, 1, False)])       # 8th grid
        self.assertEqual(sp.split_values(0, 6, 4, rest=True), [(0, 4, False), (4, 2, False)])   # rests have no dots
        for start in range(16):
            for length in range(1, 17 - start):
                parts = sp.split_values(start, length, 4)
                self.assertEqual(sum(p[1] for p in parts), length)
                self.assertEqual(parts[0][0], start)

    def test_layout_ties_and_rests(self):
        import sheet_pdf as sp
        notes = [{"slot": 12, "length": 8, "midi": 60}, {"slot": 40, "length": 4, "midi": 62}]
        bars = sp.layout(notes, 16, 4)
        self.assertEqual(len(bars), 3)
        self.assertEqual([(e["start"], e["slots"], e["midi"], e["tie"]) for e in bars[0]],
                         [(0, 8, None, False), (8, 4, None, False), (12, 4, 60, True)])
        self.assertEqual((bars[1][0]["midi"], bars[1][0]["slots"], bars[1][0]["tie"]), (60, 4, False))
        for bar in bars:                                       # every bar is full
            self.assertEqual(sum(e["slots"] for e in bar), 16)

    def test_staff_positions(self):
        import sheet_pdf as sp
        self.assertEqual(sp.staff_pos(64, False), (30, 0))      # E4, bottom line of the treble staff
        self.assertEqual(sp.staff_pos(60, False), (28, 0))      # middle C, one ledger line below
        self.assertEqual(sp.staff_pos(61, False), (28, 1))      # C sharp
        self.assertEqual(sp.staff_pos(61, True), (29, -1))      # D flat
        self.assertEqual(sp.staff_pos(43, False), (18, 0))      # G2, bottom line of the bass staff

    def test_pdfs_are_written(self):
        import chart_pdf
        import sheet_pdf as sp
        d = tempfile.mkdtemp()
        try:
            notes = [{"slot": i * 3, "length": 1 + i % 5, "midi": 55 + (i * 5) % 30} for i in range(120)]
            pages = sp.write_sheet_pdf(os.path.join(d, "s.pdf"), notes, 16, 120, "T", flats=True)
            self.assertGreaterEqual(pages, 2)
            self.assertEqual(sp.write_sheet_pdf(os.path.join(d, "e.pdf"), [], 16, 120, "Empty"), 1)
            chart = [{"time_ms": 500 + i * 250, "lane": i % 4, "duration_ms": 600 if i % 9 == 0 else 0} for i in range(400)]
            pages = chart_pdf.write_chart_pdf(os.path.join(d, "c.pdf"), chart, rhythm.Tempo(120.0, 0.5), 110.0, "T")
            self.assertEqual(pages, 3)                          # 55 bars, 24 per page
            for f in ("s.pdf", "e.pdf", "c.pdf"):
                with open(os.path.join(d, f), "rb") as fh:
                    self.assertEqual(fh.read(5), b"%PDF-")
        finally:
            shutil.rmtree(d)


class MidiTests(unittest.TestCase):
    def test_midi_file(self):
        d = tempfile.mkdtemp()
        try:
            p = os.path.join(d, "m.mid")
            midi.write_midi(p, [{"slot": 0, "length": 4, "midi": 60}, {"slot": 4, "length": 2, "midi": 64}], 120.0, grid=16)
            with open(p, "rb") as fh:
                data = fh.read()
            self.assertEqual(data[:4], b"MThd")
            fmt, tracks, ppq = struct.unpack(">HHH", data[8:14])
            self.assertEqual((fmt, tracks, ppq), (0, 1, 480))
            self.assertEqual(data[14:18], b"MTrk")
            self.assertEqual(struct.unpack(">I", data[18:22])[0], len(data) - 22)
            self.assertIn(b"\xff\x51\x03\x07\xa1\x20", data)           # 500000 microseconds per beat = 120 BPM
            self.assertIn(bytes([0x90, 60, 90]), data)
            self.assertIn(b"\x83\x60" + bytes([0x80, 60, 0]), data)    # note off 480 ticks later (one quarter note)
            self.assertEqual(midi._varlen(0), b"\x00")
            self.assertEqual(midi._varlen(128), b"\x81\x00")
        finally:
            shutil.rmtree(d)


class EndToEndTests(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.mkdtemp()
        y, _, _ = synth.song(bpm=128, bars=20, offset=0.35)
        self.wav = os.path.join(self.d, "My Test-Song.wav")
        synth.save_wav(self.wav, y)

    def tearDown(self):
        shutil.rmtree(self.d)

    def _args(self, *extra):
        return transcribe.build_parser().parse_args(list(extra))

    def test_wav_to_chart(self):
        out = os.path.join(self.d, "out")
        r = transcribe.run(self._args(self.wav, "--out", out, "--level", "4"), log=lambda s: None)
        self.assertEqual(r["id"], "my_test_song")
        chart = _json(os.path.join(out, "my_test_song.json"))
        self.assertEqual(chart["lanes"], 4)
        self.assertEqual(chart["title"], "My Test-Song")
        self.assertGreater(len(chart["notes"]), 40)
        for n in chart["notes"]:
            self.assertIn(n["type"], ("tap", "hold"))
            self.assertIn(n["lane"], (0, 1, 2, 3))
        self.assertAlmostEqual(r["bpm"], 128.0, delta=0.1)
        self.assertEqual(os.listdir(out), ["my_test_song.json"])      # no PDFs unless asked

    @unittest.skipUnless(HAVE_PDF, "reportlab not installed")
    def test_pdf_option_writes_both(self):
        out = os.path.join(self.d, "out")
        transcribe.run(self._args(self.wav, "--out", out, "--pdf"), log=lambda s: None)
        self.assertEqual(sorted(os.listdir(out)), ["my_test_song.json", "my_test_song_chart.pdf",
                                                    "my_test_song_melody.mid", "my_test_song_melody.pdf"])

    @unittest.skipUnless(_has_encoder("libmp3lame") and _has_encoder("libvorbis"), "ffmpeg with mp3 and ogg needed")
    def test_mp3_into_project(self):
        mp3 = os.path.join(self.d, "song one.mp3")
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", self.wav, "-c:a", "libmp3lame", "-b:a", "160k", mp3], check=True)
        proj = os.path.join(self.d, "proj")
        res = os.path.join(proj, "Assets", "Resources")
        os.makedirs(res)
        with open(os.path.join(res, "songs.json"), "w") as f:
            json.dump({"songs": [{"id": "demo", "title": "Demo"}]}, f)
        out = os.path.join(self.d, "out")
        a = self._args(mp3, "--out", out, "--project", proj, "--title", "Song One", "--artist", "Me", "--level", "6")
        r = transcribe.run(a, log=lambda s: None)
        self.assertTrue(r["added"])
        self.assertAlmostEqual(r["bpm"], 128.0, delta=0.1)
        self.assertLess(abs(_wrap(r["offset"] - 0.35, 60 / 128.0)) * 1000, 15.0)   # mp3 -> ogg must not shift the beat
        self.assertTrue(os.path.exists(os.path.join(res, "Audio", "song_one.ogg")))
        self.assertTrue(os.path.exists(os.path.join(res, "Charts", "song_one.json")))
        songs = _json(os.path.join(res, "songs.json"))["songs"]
        self.assertEqual([s["id"] for s in songs], ["demo", "song_one"])
        e = songs[1]
        self.assertEqual((e["title"], e["artist"], e["audio"], e["chart"], e["level"]), ("Song One", "Me", "Audio/song_one", "song_one", 6))
        self.assertAlmostEqual(e["bpm"], 128.0, delta=0.1)
        r2 = transcribe.run(a, log=lambda s: None)                      # again: no second entry
        self.assertFalse(r2["added"])
        self.assertEqual(len(_json(os.path.join(res, "songs.json"))["songs"]), 2)

    def test_errors_are_readable(self):
        with self.assertRaises(audio.AudioError):
            transcribe.run(self._args(os.path.join(self.d, "nope.mp3")), log=lambda s: None)
        with self.assertRaises(ValueError):
            transcribe.run(self._args(self.wav, "--project", self.d), log=lambda s: None)     # not a project
        quiet = os.path.join(self.d, "quiet.wav")
        synth.save_wav(quiet, np.zeros(3 * synth.SR, np.float32))
        with self.assertRaises((ValueError, audio.AudioError)):
            transcribe.run(self._args(quiet), log=lambda s: None)
        self.assertEqual(transcribe.main([self.wav, "--level", "11"]), 2)


if __name__ == "__main__":
    unittest.main()
