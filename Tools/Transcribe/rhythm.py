"""Beat finding and 4-lane chart generation from audio. Plain numpy, no machine learning.

Steps: spectrogram -> onset strength (how much louder each frequency band got) -> tempo and beat phase
(steady tempo assumed) -> pick the strongest onsets on the beat grid -> lanes from pitch height (low = left).
"""
import numpy as np

from audio import stft_mag

N_FFT = 1024
HOP = 256
N_BANDS = 24

# The onset detector reacts before the real attack (the analysis window sees it coming). Measured with
# click tracks in test_transcribe.py, added back to every detected time.
ONSET_BIAS_SEC = 0.011


class Analysis:
    pass


def _moving_average(x, n):
    n = max(1, int(n)) | 1
    k = np.ones(n) / n
    return np.convolve(x, k, mode="same")


def analyse(y, sr):
    """Onset strength per frame, per band, and loudness."""
    a = Analysis()
    a.sr = sr
    a.fr = sr / HOP                         # frames per second
    a.duration = len(y) / sr
    mag = stft_mag(y, N_FFT, HOP)
    peak = float(mag.max())
    if peak <= 1e-9:
        raise ValueError("Audio is silent.")
    mag /= peak
    a.rms = np.sqrt((mag * mag).sum(axis=1))
    logm = np.log1p(1000.0 * mag)

    freqs = np.fft.rfftfreq(N_FFT, 1.0 / sr)
    edges = np.geomspace(40.0, min(10000.0, sr * 0.47), N_BANDS + 1)
    flux = np.zeros_like(logm)
    flux[1:] = np.maximum(0.0, logm[1:] - logm[:-1])
    band = np.zeros((len(logm), N_BANDS), np.float32)
    for b in range(N_BANDS):
        cols = np.where((freqs >= edges[b]) & (freqs < edges[b + 1]))[0]
        if len(cols):
            band[:, b] = flux[:, cols].mean(axis=1)
    a.band_flux = band
    total = band.sum(axis=1).astype(np.float64)
    env = np.maximum(0.0, total - _moving_average(total, 0.4 * a.fr))
    pos = env[env > 0]
    env /= (np.percentile(pos, 95) if len(pos) else 1.0) + 1e-9
    a.env = env
    low = band[:, :6].sum(axis=1).astype(np.float64)      # kick drum range, used to guess bar starts
    a.low_env = np.maximum(0.0, low - _moving_average(low, 0.4 * a.fr))
    return a


def _interp(x, idx):
    idx = np.asarray(idx, dtype=np.float64)
    i0 = np.clip(np.floor(idx).astype(int), 0, len(x) - 1)
    i1 = np.clip(i0 + 1, 0, len(x) - 1)
    f = idx - np.floor(idx)
    ok = (idx >= 0) & (idx <= len(x) - 1)
    return np.where(ok, x[i0] * (1 - f) + x[i1] * f, 0.0)


class Tempo:
    def __init__(self, bpm, offset, fit=0.0, chance=0.0, bar_beat=0):
        self.bpm = bpm
        self.offset = offset        # seconds, time of one beat (the first one at or after 0)
        self.fit = fit              # share of strong onsets that sit on the 8th-note grid
        self.chance = chance        # what fit would be for random onsets
        self.bar_beat = bar_beat    # which beat index (0..beats_per_bar-1) starts a bar

    @property
    def beat(self):
        return 60.0 / self.bpm

    def bar_origin(self, beats_per_bar=4):
        """Time where bar 1 starts. The first downbeat, or one bar earlier when more than a beat of music
        comes before it (a pickup)."""
        bar = self.beat * beats_per_bar
        first = (self.offset + self.bar_beat * self.beat) % bar
        return first - bar if first > self.beat + 0.02 else first

    @property
    def steady(self):
        return self.fit >= max(0.55, self.chance + 0.25)


def _coarse_bpm(env, fr):
    x = env - env.mean()
    n = len(x)
    size = 1 << int(np.ceil(np.log2(2 * n)))
    spec = np.fft.rfft(x, size)
    ac = np.fft.irfft(spec * np.conj(spec))[:n]
    ac /= ac[0] + 1e-12
    bpms = np.arange(60.0, 200.01, 0.25)
    lags = 60.0 * fr / bpms
    score = _interp(ac, lags) + 0.5 * _interp(ac, 2 * lags) + 0.25 * _interp(ac, 4 * lags)
    score *= np.exp(-0.5 * (np.log2(bpms / 120.0) / 0.8) ** 2)      # prefer tempos people tap to
    return float(bpms[int(np.argmax(score))])


def _refine_bpm(env, fr, bpm, span=0.03, steps=1201):
    """Fine tempo: strongest periodicity near bpm. A small error here drifts off the beat over a whole song."""
    n = np.arange(len(env), dtype=np.float64)
    f0 = bpm / 60.0 / fr
    deltas = np.linspace(-span, span, steps)
    score = np.zeros(steps)
    x = env.astype(np.float64)
    for a in range(0, steps, 64):
        f = f0 * (1.0 + deltas[a:a + 64])
        for h in (1.0, 2.0, 4.0):
            ph = np.exp(-2j * np.pi * h * np.outer(f, n))
            score[a:a + 64] += np.abs(ph @ x)
    i = int(np.argmax(score))
    d = deltas[i]
    if 0 < i < steps - 1:                    # parabola through the peak for a finer value
        s0, s1, s2 = score[i - 1], score[i], score[i + 1]
        den = s0 - 2 * s1 + s2
        if abs(den) > 1e-12:
            d += 0.5 * (s0 - s2) / den * (deltas[1] - deltas[0])
    return bpm * (1.0 + d)


def _phase(env, fr, bpm):
    period = 60.0 * fr / bpm
    sm = _moving_average(env, 3)
    k = np.arange(0, int(len(env) / period) + 1)
    phis = np.arange(0.0, period, 0.25)
    sums = np.array([_interp(sm, p + k * period).sum() for p in phis])
    return float(phis[int(np.argmax(sums))]) / fr


def _fit(env, fr, bpm, offset):
    peaks = np.where((env[1:-1] > env[:-2]) & (env[1:-1] >= env[2:]) & (env[1:-1] > 0.3))[0] + 1
    eighth = 30.0 / bpm
    chance = min(1.0, 0.060 / eighth)
    if len(peaks) < 8:
        return 0.0, chance
    t = peaks / fr
    d = np.abs((t - offset + eighth / 2) % eighth - eighth / 2)
    w = env[peaks]
    return float((w * (d <= 0.030)).sum() / w.sum()), chance


def find_tempo(an, bpm=None, offset=None, beats_per_bar=4):
    """Tempo, beat phase and bar phase. bpm / offset (seconds) given = used as they are."""
    if bpm is None:
        bpm = _refine_bpm(an.env, an.fr, _coarse_bpm(an.env, an.fr))
    if offset is None:
        offset = _phase(an.env, an.fr, bpm) + ONSET_BIAS_SEC
    beat = 60.0 / bpm
    offset = offset % beat
    fit, chance = _fit(an.env, an.fr, bpm, offset - ONSET_BIAS_SEC)

    # Bar start: the beat position where the low band (kick) hits hardest.
    n_beats = int((an.duration - offset) / beat)
    k = np.arange(n_beats)
    low = _interp(_moving_average(an.low_env, 5), (offset - ONSET_BIAS_SEC + k * beat) * an.fr)
    sums = [low[j::beats_per_bar].mean() if len(low[j::beats_per_bar]) else 0.0 for j in range(beats_per_bar)]
    return Tempo(bpm, offset, fit, chance, int(np.argmax(sums)))


def level_density(level):
    """Notes per second aimed for at a difficulty level (1..10)."""
    return 0.8 + 0.6 * (max(1, min(10, level)) - 1)


def _centroid(an, frame):
    a, b = max(0, frame - 2), min(len(an.band_flux), frame + 3)
    w = an.band_flux[a:b].sum(axis=0)
    s = w.sum()
    return float((w * np.arange(N_BANDS)).sum() / s) if s > 0 else N_BANDS / 2.0


def make_chart(an, tempo, level=3, lanes=4, snap=None, holds=True, start_sec=0.5):
    """Returns notes: list of {time_ms, lane, duration_ms}, sorted. snap None = snap when the tempo is steady."""
    if snap is None:
        snap = tempo.steady
    beat = tempo.beat
    target = int(level_density(level) * an.duration)
    env = an.env
    cands = []          # (time_sec, strength, frame, slot or None)

    if snap:
        slot = beat / 4.0
        first = int(np.ceil((start_sec - tempo.offset) / slot))
        last = int((an.duration - 0.3 - tempo.offset) / slot)
        slots = np.arange(first, last + 1)
        if len(slots) == 0:
            return []
        times = tempo.offset + slots * slot
        centre = np.round((times - ONSET_BIAS_SEC) * an.fr).astype(int)
        w = 2 if slot * an.fr > 6 else 1
        strength = np.zeros(len(slots))
        where = np.zeros(len(slots), int)
        for d in range(-w, w + 1):
            idx = np.clip(centre + d, 0, len(env) - 1)
            v = env[idx]
            better = v > strength
            strength[better] = v[better]
            where[better] = idx[better]
        step = 4 if level <= 2 else (2 if level <= 5 else 1)        # beats, 8ths, 16ths
        weight = np.where(slots % 4 == 0, 1.0, np.where(slots % 2 == 0, 0.8, 0.6))
        local = _moving_average(strength, 16 * 8) + 0.1 * (strength.mean() + 1e-9)
        score = strength / local * weight
        floor = 0.1 * np.percentile(strength, 90)
        ok = (slots % step == 0) & (strength > floor)
        for i in np.where(ok)[0]:
            cands.append((float(times[i]), float(score[i]), int(where[i]), int(slots[i])))
    else:
        gap = max(2, int(an.fr * max(0.07, 0.6 / (level_density(level) + 1.0))))
        peaks = np.where((env[1:-1] > env[:-2]) & (env[1:-1] >= env[2:]) & (env[1:-1] > 0.15))[0] + 1
        local = _moving_average(env, an.fr * 8) + 0.1 * env.mean()
        taken = []
        for p in sorted(peaks, key=lambda q: -env[q]):
            t = p / an.fr + ONSET_BIAS_SEC
            if t < start_sec or t > an.duration - 0.3:
                continue
            if any(abs(p - q) < gap for q in taken):
                continue
            taken.append(p)
            cands.append((float(t), float(env[p] / local[p]), int(p), None))

    cands.sort(key=lambda c: -c[1])
    picked = sorted(cands[:max(0, target)], key=lambda c: c[0])
    if not picked:
        return []

    # Lanes: pitch height of what got louder. Quartiles over the song, so all lanes get used.
    cents = np.array([_centroid(an, c[2]) for c in picked])
    edges = np.quantile(cents, np.linspace(0, 1, lanes + 1)[1:-1])
    lane_of = np.searchsorted(edges, cents, side="right")

    notes = []
    prev_lane, prev_t, run = -1, -9.0, 0
    for (t, s, frame, slot_i), lane in zip(picked, lane_of):
        lane = int(lane)
        if lane == prev_lane and t - prev_t < 0.55 * beat:
            run += 1
            if run >= 2 or t - prev_t < 0.3 * beat:      # no fast repeats on one finger
                lane = lane + 1 if lane < lanes - 1 and (lane == 0 or (slot_i or 0) % 2 == 0) else lane - 1
                run = 0
        else:
            run = 0
        notes.append({"time_ms": int(round(t * 1000)), "lane": lane, "duration_ms": 0, "_s": s, "_slot": slot_i})
        prev_lane, prev_t = lane, t

    if holds and level >= 3:
        frames_per_sec = an.fr
        for i, n in enumerate(notes):
            t = n["time_ms"] / 1000.0
            nxt = notes[i + 1]["time_ms"] / 1000.0 if i + 1 < len(notes) else an.duration
            room = nxt - t
            if room < 1.5 * beat:
                continue
            f0 = int(t * frames_per_sec)
            head = an.rms[f0:f0 + max(2, int(0.1 * frames_per_sec))]
            body = an.rms[f0 + int(0.15 * frames_per_sec):int((t + room - 0.5 * beat) * frames_per_sec)]
            if len(head) == 0 or len(body) < 4:
                continue
            ref = head.max()
            keep = np.where(body < 0.5 * ref)[0]
            sustain = (keep[0] if len(keep) else len(body)) / frames_per_sec + 0.15
            length = min(sustain, room - 0.5 * beat, 4 * beat)
            if snap:
                length = np.floor(length / (beat / 2.0)) * (beat / 2.0)
            if length >= beat:
                n["duration_ms"] = int(round(length * 1000))

    if level >= 7 and lanes >= 4:                       # some double notes on strong beats
        strong = np.quantile([n["_s"] for n in notes], 0.88)
        extra = []
        for n in notes:
            on_beat = n["_slot"] is None or n["_slot"] % 4 == 0
            if n["_s"] >= strong and on_beat and n["duration_ms"] == 0:
                extra.append({"time_ms": n["time_ms"], "lane": (n["lane"] + 2) % lanes, "duration_ms": 0})
        notes += extra

    for n in notes:
        n.pop("_s", None)
        n.pop("_slot", None)
    notes.sort(key=lambda n: (n["time_ms"], n["lane"]))
    return notes


def preview_start(an, tempo, window=15.0):
    """Start of the loudest stretch, moved back to a bar line. Good spot for the song select preview."""
    n = int(window * an.fr)
    if len(an.rms) <= n:
        return 0.0
    c = np.concatenate([[0.0], np.cumsum(an.rms.astype(np.float64) ** 2)])
    start = int(np.argmax(c[n:] - c[:-n])) / an.fr
    bar = tempo.beat * 4
    origin = tempo.bar_origin(4)
    return round(max(0.0, origin + np.floor((start - origin) / bar) * bar), 2)
