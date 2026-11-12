"""Audio loading and spectrograms. Needs numpy. ffmpeg decodes everything that is not a plain WAV."""
import os
import shutil
import subprocess

import numpy as np

SR = 22050


class AudioError(Exception):
    pass


def have_ffmpeg():
    return shutil.which("ffmpeg") is not None


def load_audio(path, sr=SR):
    """Decode any audio file to mono float32 at sr. Returns a numpy array."""
    if not os.path.exists(path):
        raise AudioError("File not found: " + path)
    if have_ffmpeg():
        p = subprocess.run(
            ["ffmpeg", "-v", "error", "-nostdin", "-i", path, "-f", "f32le", "-acodec", "pcm_f32le",
             "-ac", "1", "-ar", str(sr), "-"],
            capture_output=True)
        if p.returncode != 0 or not p.stdout:
            raise AudioError("ffmpeg could not decode the file: " + (p.stderr.decode(errors="replace").strip() or "no output"))
        y = np.frombuffer(p.stdout, dtype="<f4").astype(np.float32)
    elif path.lower().endswith(".wav"):
        from scipy.io import wavfile
        from scipy.signal import resample_poly
        from math import gcd
        rate, data = wavfile.read(path)
        y = data.astype(np.float32)
        if data.dtype.kind == "i":
            y /= float(np.iinfo(data.dtype).max)
        elif data.dtype.kind == "u":
            y = (y - 128.0) / 128.0
        if y.ndim > 1:
            y = y.mean(axis=1)
        if rate != sr:
            g = gcd(int(rate), int(sr))
            y = resample_poly(y, sr // g, rate // g).astype(np.float32)
    else:
        raise AudioError("ffmpeg not found. Install ffmpeg to read mp3/ogg/m4a, or give a .wav file.")
    if len(y) < sr:
        raise AudioError("Audio is shorter than one second.")
    return y


def convert_audio(src, dst):
    """Re-encode src to dst (format from the extension) with ffmpeg. Raises AudioError."""
    if not have_ffmpeg():
        raise AudioError("ffmpeg not found, cannot convert audio.")
    args = ["ffmpeg", "-v", "error", "-nostdin", "-y", "-i", src, "-vn"]
    if dst.lower().endswith(".ogg"):
        args += ["-c:a", "libvorbis", "-q:a", "6"]
    p = subprocess.run(args + [dst], capture_output=True)
    if p.returncode != 0 or not os.path.exists(dst):
        raise AudioError("ffmpeg could not write " + dst + ": " + p.stderr.decode(errors="replace").strip())


def stft_mag(y, n_fft, hop):
    """Magnitude spectrogram, shape (frames, n_fft/2+1), float32. Frame i is centred on sample i*hop."""
    pad = n_fft // 2
    yp = np.concatenate([np.zeros(pad, np.float32), y.astype(np.float32), np.zeros(pad + n_fft, np.float32)])
    n_frames = 1 + len(y) // hop
    win = np.hanning(n_fft).astype(np.float32)
    view = np.lib.stride_tricks.sliding_window_view(yp, n_fft)[::hop][:n_frames]
    out = np.empty((n_frames, n_fft // 2 + 1), np.float32)
    step = 1024
    for a in range(0, n_frames, step):
        out[a:a + step] = np.abs(np.fft.rfft(view[a:a + step] * win, axis=1))
    return out
