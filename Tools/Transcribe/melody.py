"""Melody draft: the single most prominent pitch line of a recording, as notes on the beat grid.

Method: for every pitch, add up the energy at its harmonics ("salience"), follow the strongest smooth path
through time, then cut that path into notes on a 16th or 8th note grid.
This is not machine learning. One line only, no chords. Expect wrong octaves and missed notes on dense mixes:
the result is a starting point to correct by ear, not a finished score.
"""
import numpy as np

from audio import stft_mag

N_FFT = 4096
HOP = 512
BINS_PER_SEMI = 3
NAMES_SHARP = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
NAMES_FLAT = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"]


def pitch_track(y, sr, lo=52, hi=88):
    """Returns (midi pitch per frame as float, salience per frame, frames per second). lo/hi are MIDI notes."""
    mag = stft_mag(y, N_FFT, HOP)
    mag /= float(mag.max()) + 1e-9
    energy = np.sqrt((mag * mag).sum(axis=1))
    comp = np.power(mag, 0.6)
    n_bins = mag.shape[1]

    pitches = lo + np.arange((hi - lo) * BINS_PER_SEMI + 1) / BINS_PER_SEMI
    f0 = 440.0 * 2.0 ** ((pitches - 69.0) / 12.0)
    W = np.zeros((len(pitches), n_bins), np.float32)
    rows = np.arange(len(pitches))
    for h in range(1, 9):
        pos = f0 * h * N_FFT / sr
        ok = pos < n_bins - 2
        i0 = np.floor(pos).astype(int)
        fr = (pos - i0).astype(np.float32)
        w = 0.8 ** (h - 1)
        np.add.at(W, (rows[ok], i0[ok]), w * (1 - fr[ok]))
        np.add.at(W, (rows[ok], i0[ok] + 1), w * fr[ok])
    sal = comp @ W.T                                    # (frames, pitches)

    # Smooth path: big jumps cost, staying is free.
    T, P = sal.shape
    norm = sal / (sal.max(axis=1, keepdims=True) + 1e-9)
    d = np.abs(np.arange(P)[:, None] - np.arange(P)[None, :]) / BINS_PER_SEMI
    trans = -0.06 * np.minimum(d, 12.0)                 # per semitone, capped at an octave
    score = norm[0].astype(np.float64).copy()
    back = np.zeros((T, P), np.int16)
    for t in range(1, T):
        m = score[None, :] + trans                      # m[to, from]
        back[t] = np.argmax(m, axis=1)
        score = m[np.arange(P), back[t]] + norm[t]
    path = np.zeros(T, int)
    path[-1] = int(np.argmax(score))
    for t in range(T - 1, 0, -1):
        path[t - 1] = back[t, path[t]]
    strength = sal[np.arange(T), path] * (energy > 0.02 * energy.max())
    return pitches[path], strength, sr / HOP


def to_notes(pitch, strength, fr, tempo, grid=16, beats_per_bar=4, origin=None, voiced_ratio=0.5):
    """Cut the pitch path into notes on the grid.
    Returns (notes, slots_per_bar): notes = list of {slot, length, midi}; slot 0 is the start of bar 1."""
    per_beat = grid // 4
    slot_len = tempo.beat / per_beat
    if origin is None:
        origin = tempo.bar_origin(beats_per_bar)
    duration = len(pitch) / fr
    n_slots = int((duration - origin) / slot_len)
    if n_slots <= 0:
        return [], beats_per_bar * per_beat

    loud = strength[strength > 0]
    thr = voiced_ratio * (np.percentile(loud, 80) if len(loud) else 1.0)
    voiced = strength > thr
    tuning = 0.0
    if voiced.any():                                    # recording tuned a bit sharp or flat
        dev = pitch[voiced] - np.round(pitch[voiced])
        tuning = float(np.median(dev))

    slot_midi = np.full(n_slots, -1, int)
    slot_str = np.zeros(n_slots)
    for s in range(n_slots):
        a = int(round((origin + s * slot_len) * fr))
        b = int(round((origin + (s + 1) * slot_len) * fr))
        a, b = max(0, a), min(len(pitch), max(b, a + 1))
        if a >= b:
            continue
        v = voiced[a:b]
        if v.mean() >= 0.5:
            slot_midi[s] = int(np.round(np.median(pitch[a:b][v]) - tuning))
            slot_str[s] = strength[a:b][v].mean()

    # Clean up: a one-slot blip between two slots of the same pitch is the tracker wobbling.
    for s in range(1, n_slots - 1):
        if slot_midi[s] != slot_midi[s - 1] and slot_midi[s - 1] == slot_midi[s + 1] and slot_midi[s - 1] >= 0:
            slot_midi[s] = slot_midi[s - 1]
    # A single slot almost exactly an octave away from both neighbours is the tracker on the wrong octave.
    for s in range(1, n_slots - 1):
        m, p, q = slot_midi[s], slot_midi[s - 1], slot_midi[s + 1]
        if m >= 0 and p >= 0 and q >= 0:
            for shift in (-12, 12):
                if abs(m + shift - p) <= 2 and abs(m + shift - q) <= 2:
                    slot_midi[s] = m + shift
                    break

    notes = []
    s = 0
    while s < n_slots:
        m = slot_midi[s]
        if m < 0:
            s += 1
            continue
        e = s + 1
        while e < n_slots and slot_midi[e] == m:
            # same pitch played again: the strength dips and comes back
            if slot_str[e] > 1.6 * slot_str[e - 1] and slot_str[e - 1] < 0.6 * slot_str[s:e].max():
                break
            e += 1
        notes.append({"slot": s, "length": e - s, "midi": int(m)})
        s = e
    if grid >= 16:                                      # lone 16th notes with silence on both sides are mostly noise
        keep = []
        for i, n in enumerate(notes):
            alone_before = i == 0 or notes[i - 1]["slot"] + notes[i - 1]["length"] < n["slot"]
            alone_after = i == len(notes) - 1 or notes[i + 1]["slot"] > n["slot"] + n["length"]
            if n["length"] == 1 and alone_before and alone_after:
                continue
            keep.append(n)
        notes = keep
    return notes, beats_per_bar * per_beat


# Krumhansl key profiles: how much each scale step "belongs" in a major / minor key.
_MAJOR = np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88])
_MINOR = np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])
_FLAT_MAJOR = {5, 10, 3, 8, 1}          # F, Bb, Eb, Ab, Db major read better with flats


def guess_key(notes):
    """Returns (name like 'G major', use_flats)."""
    hist = np.zeros(12)
    for n in notes:
        hist[n["midi"] % 12] += n["length"]
    if hist.sum() == 0:
        return "unknown", False
    best = (-2.0, 0, True)
    for tonic in range(12):
        for major, prof in ((True, _MAJOR), (False, _MINOR)):
            c = np.corrcoef(hist, np.roll(prof, tonic))[0, 1]
            if c > best[0]:
                best = (c, tonic, major)
    _, tonic, major = best
    rel_major = tonic if major else (tonic + 3) % 12
    flats = rel_major in _FLAT_MAJOR
    name = (NAMES_FLAT if flats else NAMES_SHARP)[tonic] + (" major" if major else " minor")
    return name, flats


def note_name(midi, flats=False):
    return (NAMES_FLAT if flats else NAMES_SHARP)[midi % 12] + str(midi // 12 - 1)
