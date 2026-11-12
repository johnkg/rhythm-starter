"""Melody draft as simple sheet music (one staff). Needs reportlab. Draws its own note shapes, so no music
program is needed. Plain engraving: no beams, no key signature (every sharp/flat is written at the note).
For a clean score, open the .mid file in MuseScore instead."""
import math
import os

from reportlab.lib import colors
from reportlab.lib.pagesizes import A4, landscape
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas

SP = 6.0                      # distance between two staff lines, points
BARS_PER_SYSTEM = 4

# pitch class -> (letter step C=0..B=6, accidental)
_SHARP = [(0, 0), (0, 1), (1, 0), (1, 1), (2, 0), (3, 0), (3, 1), (4, 0), (4, 1), (5, 0), (5, 1), (6, 0)]
_FLAT = [(0, 0), (1, -1), (1, 0), (2, -1), (2, 0), (3, 0), (4, -1), (4, 0), (5, -1), (5, 0), (6, -1), (6, 0)]

_CLEF_FONTS = [
    "/usr/share/fonts/truetype/freefont/FreeSerif.ttf",
    "/usr/share/fonts/opentype/freefont/FreeSerif.otf",
    "/usr/share/fonts/truetype/noto/NotoMusic-Regular.ttf",
    "/usr/share/fonts/truetype/ancient-scripts/Symbola_hint.ttf",
    "C:/Windows/Fonts/seguisym.ttf",
    "/System/Library/Fonts/Apple Symbols.ttf",
    "/Library/Fonts/FreeSerif.ttf",
]
_clef_font = None


def _find_clef_font():
    """A font that has the clef characters, or '' (then a plain letter is drawn instead)."""
    global _clef_font
    if _clef_font is None:
        _clef_font = ""
        for p in _CLEF_FONTS:
            if not os.path.exists(p):
                continue
            try:
                f = TTFont("RhythmClef", p)
                if 0x1D11E in f.face.charToGlyph and 0x1D122 in f.face.charToGlyph:
                    pdfmetrics.registerFont(f)
                    _clef_font = "RhythmClef"
                    break
            except Exception:
                continue
    return _clef_font


def staff_pos(midi, flats):
    """(position, accidental). Position counts lines and spaces upward, C0 = 0, so E4 = 30."""
    letter, acc = (_FLAT if flats else _SHARP)[midi % 12]
    return (midi // 12 - 1) * 7 + letter, acc


def split_values(start, length, per_beat, rest=False):
    """Cut a note of `length` slots starting at `start` (inside one bar) into drawable values.
    Returns list of (start, slots, dotted). per_beat = slots in a quarter note."""
    q = per_beat
    options = []                                   # (slots, dotted, allowed start)
    options.append((4 * q, False, lambda s: s % (4 * q) == 0))
    if not rest:
        options.append((3 * q, True, lambda s: s % q == 0))
    options.append((2 * q, False, lambda s: s % q == 0 if not rest else s % (2 * q) == 0))
    if not rest and q % 2 == 0:
        options.append((q * 3 // 2, True, lambda s: s % q == 0))
    options.append((q, False, lambda s: s % max(1, q // 2) == 0 if not rest else s % q == 0))
    if q % 4 == 0 and not rest:
        options.append((q * 3 // 4, True, lambda s: s % q == 0))
    if q % 2 == 0:
        options.append((q // 2, False, lambda s: s % (q // 2) == 0))
    if q % 4 == 0:
        options.append((q // 4, False, lambda s: True))
    out = []
    while length > 0:
        for slots, dotted, ok in options:
            if slots <= length and ok(start):
                out.append((start, slots, dotted))
                start += slots
                length -= slots
                break
        else:                                       # cannot happen while the smallest value is one slot
            out.append((start, 1, False))
            start += 1
            length -= 1
    return out


def layout(notes, slots_per_bar, per_beat):
    """Notes -> bars. Each bar is a list of elements:
    {start, slots, dotted, midi (None = rest), tie (True = tied to the next element)}."""
    if not notes:
        return []
    end = max(n["slot"] + n["length"] for n in notes)
    n_bars = int(math.ceil(end / slots_per_bar))
    grid = [None] * (n_bars * slots_per_bar)        # per slot: (note index, midi)
    for i, n in enumerate(notes):
        for s in range(n["slot"], n["slot"] + n["length"]):
            if 0 <= s < len(grid):
                grid[s] = (i, n["midi"])
    bars = []
    for b in range(n_bars):
        cells = grid[b * slots_per_bar:(b + 1) * slots_per_bar]
        if all(x is None for x in cells):
            bars.append([{"start": 0, "slots": slots_per_bar, "dotted": False, "midi": None, "tie": False, "whole_bar": True}])
            continue
        els = []
        s = 0
        while s < slots_per_bar:
            e = s + 1
            while e < slots_per_bar and cells[e] == cells[s]:
                e += 1
            is_rest = cells[s] is None
            parts = split_values(s, e - s, per_beat, rest=is_rest)
            for j, (st, sl, dot) in enumerate(parts):
                tie = False
                if not is_rest:
                    if j < len(parts) - 1:
                        tie = True
                    else:                            # same note goes on in the next bar
                        nxt = grid[(b + 1) * slots_per_bar] if e == slots_per_bar and (b + 1) * slots_per_bar < len(grid) else None
                        tie = nxt is not None and nxt == cells[s]
                els.append({"start": st, "slots": sl, "dotted": dot, "midi": None if is_rest else cells[s][1], "tie": tie})
            s = e
        bars.append(els)
    return bars


# ---------------------------------------------------------------- drawing

def _head(c, x, y, hollow):
    c.saveState()
    c.translate(x, y)
    c.rotate(20)
    if hollow:
        c.setLineWidth(1.3)
        c.setFillColor(colors.white)
        c.ellipse(-SP * 0.6, -SP * 0.4, SP * 0.6, SP * 0.4, stroke=1, fill=1)
    else:
        c.setFillColor(colors.black)
        c.ellipse(-SP * 0.65, -SP * 0.45, SP * 0.65, SP * 0.45, stroke=0, fill=1)
    c.restoreState()


def _flag(c, x, y, up, count):
    c.setLineWidth(1.4)
    d = -1 if up else 1
    for i in range(count):
        yy = y + d * i * SP * 0.8
        p = c.beginPath()
        p.moveTo(x, yy)
        p.curveTo(x + SP * 0.2, yy + d * SP * 0.9, x + SP * 1.2, yy + d * SP * 1.1, x + SP * 0.8, yy + d * SP * 2.2)
        c.drawPath(p, stroke=1, fill=0)


def _sharp(c, x, y):
    c.setLineWidth(0.7)
    c.line(x - SP * 0.2, y - SP * 0.95, x - SP * 0.2, y + SP * 0.85)
    c.line(x + SP * 0.2, y - SP * 0.85, x + SP * 0.2, y + SP * 0.95)
    c.setLineWidth(1.5)
    c.line(x - SP * 0.45, y - SP * 0.4, x + SP * 0.45, y - SP * 0.2)
    c.line(x - SP * 0.45, y + SP * 0.2, x + SP * 0.45, y + SP * 0.4)


def _natural(c, x, y):
    c.setLineWidth(0.7)
    c.line(x - SP * 0.22, y - SP * 0.45, x - SP * 0.22, y + SP * 1.0)
    c.line(x + SP * 0.22, y - SP * 1.0, x + SP * 0.22, y + SP * 0.45)
    c.setLineWidth(1.4)
    c.line(x - SP * 0.22, y - SP * 0.4, x + SP * 0.22, y - SP * 0.25)
    c.line(x - SP * 0.22, y + SP * 0.25, x + SP * 0.22, y + SP * 0.4)


def _flat(c, x, y):
    c.setFillColor(colors.black)
    c.setFont("Times-BoldItalic", SP * 2.3)
    c.drawCentredString(x, y - SP * 0.45, "b")


def _rest(c, x, mid_y, slots, per_beat):
    """mid_y = y of the middle staff line."""
    c.setFillColor(colors.black)
    c.setStrokeColor(colors.black)
    q = per_beat
    if slots >= 4 * q:
        c.rect(x - SP * 0.7, mid_y + SP * 0.5, SP * 1.4, SP * 0.5, stroke=0, fill=1)     # hangs under line 4
    elif slots >= 2 * q:
        c.rect(x - SP * 0.7, mid_y, SP * 1.4, SP * 0.5, stroke=0, fill=1)                # sits on the middle line
    elif slots >= q:
        c.setLineWidth(1.6)
        p = c.beginPath()
        p.moveTo(x - SP * 0.3, mid_y + SP * 1.4)
        p.lineTo(x + SP * 0.35, mid_y + SP * 0.5)
        p.lineTo(x - SP * 0.35, mid_y - SP * 0.1)
        p.lineTo(x + SP * 0.3, mid_y - SP * 0.8)
        p.curveTo(x - SP * 0.6, mid_y - SP * 0.5, x - SP * 0.5, mid_y - SP * 1.5, x + SP * 0.1, mid_y - SP * 1.6)
        c.drawPath(p, stroke=1, fill=0)
    else:
        hooks = 1 if slots * 2 >= q else 2
        c.setLineWidth(1.1)
        c.line(x + SP * 0.5, mid_y + SP * 0.9, x - SP * 0.1, mid_y - SP * (0.9 + 0.9 * (hooks - 1)))
        for i in range(hooks):
            yy = mid_y + SP * 0.6 - i * SP
            c.circle(x - SP * 0.35 - i * SP * 0.1, yy, SP * 0.27, stroke=0, fill=1)
            c.line(x - SP * 0.3 - i * SP * 0.1, yy - SP * 0.1, x + SP * 0.42 - i * SP * 0.33, yy + SP * 0.25)


def _clef(c, x, bottom_y, treble):
    font = _find_clef_font()
    c.setFillColor(colors.black)
    if font:
        if treble:
            c.setFont(font, SP * 6.6)
            c.drawString(x, bottom_y + SP * 0.9, "\U0001D11E")
        else:
            c.setFont(font, SP * 6.2)
            c.drawString(x, bottom_y - SP * 0.5, "\U0001D122")
    else:                                           # no music font on this machine: name the clef in words
        c.setFont("Times-BoldItalic", SP * 2.4)
        c.drawString(x, bottom_y + SP * (0.4 if treble else 2.4), "G" if treble else "F")
        c.setFont("Helvetica", 5)
        c.drawString(x, bottom_y - SP * 1.5, "treble" if treble else "bass")


def _tie(c, x1, x2, y, below):
    d = -1 if below else 1
    c.setLineWidth(0.9)
    p = c.beginPath()
    p.moveTo(x1, y + d * SP * 0.7)
    p.curveTo(x1 + (x2 - x1) * 0.3, y + d * SP * 1.6, x1 + (x2 - x1) * 0.7, y + d * SP * 1.6, x2, y + d * SP * 0.7)
    c.drawPath(p, stroke=1, fill=0)


def write_sheet_pdf(path, notes, slots_per_bar, bpm, title, artist="", key_name="", flats=False,
                    beats_per_bar=4, source="", note=""):
    """notes: list of {slot, length, midi}. Returns the number of pages."""
    W, H = landscape(A4)
    margin = 40
    per_beat = slots_per_bar // beats_per_bar
    bars = layout(notes, slots_per_bar, per_beat)
    c = canvas.Canvas(path, pagesize=(W, H))
    c.setTitle(title + " - melody draft")
    c.setAuthor("RhythmStarter transcriber")

    mids = sorted(n["midi"] for n in notes)
    treble = (mids[len(mids) // 2] >= 58) if mids else True
    bottom_pos = 30 if treble else 18               # bottom line: E4 in treble clef, G2 in bass clef
    system_h = 100.0
    lead = 42.0                                      # room for the clef
    bar_w = (W - 2 * margin - lead) / BARS_PER_SYSTEM
    pad = 9.0
    slot_w = (bar_w - pad - 3) / slots_per_bar

    pages = 0
    y_top = H - margin
    first_page = True
    system = 0
    n_systems = max(1, int(math.ceil(len(bars) / BARS_PER_SYSTEM)))
    pending_tie = None                               # tie running over the end of a system

    def header():
        nonlocal y_top
        c.setFillColor(colors.black)
        if first_page:
            c.setFont("Helvetica-Bold", 18)
            c.drawString(margin, H - margin - 14, title)
            c.setFont("Helvetica", 10)
            bits = [artist] if artist else []
            bits += ["Melody draft", "%.0f BPM" % bpm, "%d/4" % beats_per_bar]
            if key_name and key_name != "unknown":
                bits.append("key guess: " + key_name)
            c.drawString(margin, H - margin - 30, "    ".join(bits))
            c.setFillColor(colors.HexColor("#555555"))
            c.setFont("Helvetica", 8)
            c.drawString(margin, H - margin - 43,
                         "Made by a program listening to the recording: main melody line only, no chords. "
                         "Expect wrong or missing notes. Check by ear. " + note)
            y_top = H - margin - 62
        else:
            c.setFont("Helvetica", 9)
            c.setFillColor(colors.HexColor("#555555"))
            c.drawString(margin, H - margin - 8, title + "  (melody draft)")
            y_top = H - margin - 22

    header()
    if not bars:
        c.setFont("Helvetica", 12)
        c.setFillColor(colors.black)
        c.drawString(margin, y_top - 40, "No clear melody was found in this recording.")
        c.showPage()
        c.save()
        return 1

    while system < n_systems:
        if y_top - system_h < margin:
            c.setFillColor(colors.HexColor("#777777"))
            c.setFont("Helvetica", 7)
            c.drawRightString(W - margin, margin - 14, "Page %d" % (pages + 1))
            c.showPage()
            pages += 1
            first_page = False
            header()
        staff_top = y_top - 30
        bottom = staff_top - 4 * SP
        mid_y = bottom + 2 * SP
        x_left = margin
        c.setStrokeColor(colors.black)
        c.setLineWidth(0.6)
        for i in range(5):
            c.line(x_left, bottom + i * SP, W - margin, bottom + i * SP)
        c.line(x_left, bottom, x_left, staff_top)
        _clef(c, x_left + 4, bottom, treble)
        if system == 0:
            c.setFont("Times-Bold", SP * 2.5)
            c.setFillColor(colors.black)
            c.drawCentredString(x_left + lead - 6, bottom + 2 * SP + 0.5, str(beats_per_bar))
            c.drawCentredString(x_left + lead - 6, bottom + 0.5, "4")

        for k in range(BARS_PER_SYSTEM):
            b = system * BARS_PER_SYSTEM + k
            x0 = x_left + lead + k * bar_w
            c.setStrokeColor(colors.black)
            c.setLineWidth(0.6)
            c.line(x0 + bar_w, bottom, x0 + bar_w, staff_top)
            if b >= len(bars):
                continue
            c.setFillColor(colors.HexColor("#666666"))
            c.setFont("Helvetica", 6.5)
            c.drawString(x0 + 2, staff_top + 4, str(b + 1))
            shown = {}                               # staff position -> accidental in force in this bar
            for el in bars[b]:
                x = x0 + pad + (el["start"] + 0.5) * slot_w
                c.setStrokeColor(colors.black)
                c.setFillColor(colors.black)
                if el["midi"] is None:
                    if el.get("whole_bar"):
                        x = x0 + bar_w / 2
                    _rest(c, x, mid_y, el["slots"], per_beat)
                    continue
                pos, acc = staff_pos(el["midi"], flats)
                y = bottom + (pos - bottom_pos) * SP / 2.0
                # ledger lines
                c.setLineWidth(0.6)
                for p in range(bottom_pos - 2, pos - 1, -2):
                    yy = bottom + (p - bottom_pos) * SP / 2.0
                    c.line(x - SP, yy, x + SP, yy)
                for p in range(bottom_pos + 10, pos + 1, 2):
                    yy = bottom + (p - bottom_pos) * SP / 2.0
                    c.line(x - SP, yy, x + SP, yy)
                if pending_tie is not None and pending_tie[0] == el["midi"]:
                    _tie(c, x0 + 1, x - SP * 0.6, y, pending_tie[1])
                continued = pending_tie is not None
                pending_tie = None
                if shown.get(pos, 0) != acc and not (continued and el["start"] == 0):
                    ax = x - SP * 1.45
                    (_sharp if acc == 1 else _flat if acc == -1 else _natural)(c, ax, y)
                shown[pos] = acc
                c.setStrokeColor(colors.black)
                value = el["slots"] / float(per_beat) / (1.5 if el["dotted"] else 1.0)     # in quarter notes
                up = pos < bottom_pos + 4
                _head(c, x, y, hollow=value >= 2)
                c.setStrokeColor(colors.black)
                if value < 4:
                    c.setLineWidth(0.8)
                    sx = x + SP * 0.6 if up else x - SP * 0.6
                    tip = y + (SP * 3.4 if up else -SP * 3.4)
                    c.line(sx, y, sx, tip)
                    if value <= 0.5:
                        _flag(c, sx, tip, up, 1 if value > 0.25 else 2)
                if el["dotted"]:
                    c.setFillColor(colors.black)
                    dy = SP * 0.5 if pos % 2 == bottom_pos % 2 else 0     # on a line: move the dot into the space
                    c.circle(x + SP * 1.25, y + dy, SP * 0.2, stroke=0, fill=1)
                if el["tie"]:
                    below = up
                    nxt_x = None
                    idx = bars[b].index(el)
                    if idx + 1 < len(bars[b]):
                        nxt_x = x0 + pad + (bars[b][idx + 1]["start"] + 0.5) * slot_w
                    elif k + 1 < BARS_PER_SYSTEM and b + 1 < len(bars):
                        nxt_x = x0 + bar_w + pad + 0.5 * slot_w
                    c.setStrokeColor(colors.black)
                    if nxt_x is not None:
                        _tie(c, x + SP * 0.6, nxt_x - SP * 0.6, y, below)
                    else:                            # tie runs on to the next system
                        _tie(c, x + SP * 0.6, x0 + bar_w - 1, y, below)
                        pending_tie = (el["midi"], below)
        y_top -= system_h
        system += 1

    c.setStrokeColor(colors.black)
    c.setFillColor(colors.HexColor("#777777"))
    c.setFont("Helvetica", 7)
    foot = "Page %d" % (pages + 1)
    if source:
        foot = "Auto-transcribed from " + source + ".    " + foot
    c.drawRightString(W - margin, margin - 14, foot)
    c.showPage()
    c.save()
    return pages + 1
