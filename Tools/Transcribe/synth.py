"""Synthetic test songs with a known tempo and melody. Used by the tests only."""
import numpy as np

SR = 22050


def _tone(freq, dur, sr=SR, harmonics=(1.0, 0.5, 0.33, 0.2, 0.1), vibrato=0.0):
    t = np.arange(int(dur * sr)) / sr
    ph = 2 * np.pi * freq * t + vibrato * np.sin(2 * np.pi * 5.5 * t)
    y = sum(a * np.sin((i + 1) * ph) for i, a in enumerate(harmonics))
    env = np.minimum(1.0, t / 0.01) * np.minimum(1.0, (dur - t) / 0.03).clip(0, 1)
    return (y * env).astype(np.float32)


def _add(buf, start, sig, gain=1.0):
    a = int(round(start * SR))
    if a >= len(buf) or a < 0:
        return
    sig = sig[:len(buf) - a]
    buf[a:a + len(sig)] += gain * sig


def midi_hz(m):
    return 440.0 * 2 ** ((m - 69) / 12.0)


def click_track(bpm, seconds, offset=0.5):
    """Sharp clicks on every beat. Returns (audio, beat_times)."""
    y = np.zeros(int(seconds * SR), np.float32)
    rng = np.random.default_rng(1)
    click = (rng.standard_normal(int(0.02 * SR)) * np.exp(-np.arange(int(0.02 * SR)) / (0.004 * SR))).astype(np.float32)
    times = np.arange(offset, seconds - 0.2, 60.0 / bpm)
    for t in times:
        _add(y, t, click)
    return y, times


def song(bpm=120.0, bars=16, offset=0.4, melody=None, with_band=True, seed=3):
    """4/4 song: kick on 1 and 3, snare on 2 and 4, hats on 8ths, bass, soft chords, lead melody.
    melody: list of (start_beat, length_beats, midi). Returns (audio, melody, duration)."""
    beat = 60.0 / bpm
    total = offset + bars * 4 * beat + 1.0
    y = np.zeros(int(total * SR), np.float32)
    rng = np.random.default_rng(seed)
    if melody is None:
        scale = [60, 62, 64, 65, 67, 69, 71, 72]
        melody, b = [], 0.0
        lengths = [1, 1, 0.5, 0.5, 1, 2, 0.5, 0.5, 1]
        i = 0
        while b < bars * 4 - 2:
            L = lengths[i % len(lengths)]
            if i % 7 == 6:
                b += 1.0                                   # a rest
            melody.append((b, L, 12 + scale[(i * 3) % len(scale)]))
            b += L
            i += 1
    if with_band:
        tk = np.arange(int(0.15 * SR)) / SR
        kick = (np.sin(2 * np.pi * (50 * tk + 60 * 0.03 * (1 - np.exp(-tk / 0.03)))) * np.exp(-tk / 0.05)).astype(np.float32)
        ts = np.arange(int(0.12 * SR)) / SR
        snare = (rng.standard_normal(len(ts)) * np.exp(-ts / 0.03)).astype(np.float32)
        th = np.arange(int(0.03 * SR)) / SR
        hat = np.diff(rng.standard_normal(len(th) + 1)).astype(np.float32) * np.exp(-th / 0.008).astype(np.float32)
        roots = [36, 41, 43, 36]
        for bar in range(bars):
            for b in range(4):
                t = offset + (bar * 4 + b) * beat
                _add(y, t, kick if b % 2 == 0 else snare, 0.9 if b % 2 == 0 else 0.5)
                _add(y, t, hat, 0.25)
                _add(y, t + beat / 2, hat, 0.2)
                _add(y, t, _tone(midi_hz(roots[bar % 4]), beat * 0.9, harmonics=(1.0, 0.3)), 0.35)
            root = roots[bar % 4] + 12
            for iv in (0, 4, 7):
                _add(y, offset + bar * 4 * beat, _tone(midi_hz(root + iv), 4 * beat * 0.98, harmonics=(1.0, 0.2)), 0.07)
    for b, L, m in melody:
        _add(y, offset + b * beat, _tone(midi_hz(m), L * beat * 0.92, vibrato=0.15), 0.3)
    y /= np.abs(y).max() + 1e-9
    return (0.8 * y).astype(np.float32), melody, total


def save_wav(path, y, sr=SR):
    from scipy.io import wavfile
    wavfile.write(path, sr, (np.clip(y, -1, 1) * 32767).astype(np.int16))
