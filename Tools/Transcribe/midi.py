"""Tiny MIDI file writer (one track). Lets the melody draft open in MuseScore or any DAW for fixing."""
import struct


def _varlen(n):
    out = [n & 0x7F]
    n >>= 7
    while n:
        out.append((n & 0x7F) | 0x80)
        n >>= 7
    return bytes(reversed(out))


def write_midi(path, notes, bpm, grid=16, beats_per_bar=4, title="Melody"):
    """notes: list of {slot, length, midi}. One slot = one grid step (a 16th note when grid is 16)."""
    ppq = 480
    ticks = ppq * 4 // grid
    events = []                                         # (tick, order, bytes)
    for n in notes:
        on, off = n["slot"] * ticks, (n["slot"] + n["length"]) * ticks
        events.append((on, 1, bytes([0x90, n["midi"] & 0x7F, 90])))
        events.append((off, 0, bytes([0x80, n["midi"] & 0x7F, 0])))
    events.sort(key=lambda e: (e[0], e[1]))
    name = title.encode("ascii", "replace")[:100]
    track = b"\x00\xff\x03" + _varlen(len(name)) + name
    track += b"\x00\xff\x51\x03" + struct.pack(">I", int(round(60000000.0 / bpm)))[1:]
    track += b"\x00\xff\x58\x04" + bytes([beats_per_bar, 2, 24, 8])
    last = 0
    for tick, _, data in events:
        track += _varlen(tick - last) + data
        last = tick
    track += b"\x00\xff\x2f\x00"
    with open(path, "wb") as f:
        f.write(b"MThd" + struct.pack(">IHHH", 6, 0, 1, ppq))
        f.write(b"MTrk" + struct.pack(">I", len(track)) + track)
