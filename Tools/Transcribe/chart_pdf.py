"""Chart as a printable PDF: 4 lanes, time runs bottom to top like the game. Needs reportlab."""
import math

from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas

LANE_COLORS = ["#2f6fdb", "#e0892b", "#e0892b", "#2f6fdb"]
HOLD_ALPHA = 0.45
COLS_PER_PAGE = 6
BARS_PER_COL = 4


def _mmss(t):
    t = max(0.0, t)
    return "%d:%02d" % (int(t // 60), int(t % 60))


def write_chart_pdf(path, notes, tempo, duration, title, artist="", level=1, lanes=4, beats_per_bar=4,
                    snapped=True, source=""):
    """notes: list of {time_ms, lane, duration_ms}. Returns the number of pages."""
    W, H = A4
    margin = 36
    bar_len = tempo.beat * beats_per_bar
    origin = tempo.bar_origin(beats_per_bar)
    n_bars = max(1, int(math.ceil((duration - origin) / bar_len)))
    n_cols = int(math.ceil(n_bars / BARS_PER_COL))
    n_pages = int(math.ceil(n_cols / COLS_PER_PAGE))
    col_time = bar_len * BARS_PER_COL

    c = canvas.Canvas(path, pagesize=A4)
    c.setTitle(title + " - chart")
    c.setAuthor("RhythmStarter transcriber")

    gutter = 26
    col_w = (W - 2 * margin) / COLS_PER_PAGE
    strip_w = col_w - gutter - 10
    lane_w = strip_w / lanes
    holds = sum(1 for n in notes if n["duration_ms"] > 0)

    for page in range(n_pages):
        top = H - margin
        if page == 0:
            c.setFont("Helvetica-Bold", 18)
            c.setFillColor(colors.black)
            c.drawString(margin, top - 16, title)
            c.setFont("Helvetica", 10)
            line = ("%s    " % artist if artist else "") + "Level %d    %.1f BPM    first beat %d ms    %d notes (%d holds)    %s" % (
                level, tempo.bpm, round(tempo.offset * 1000), len(notes), holds, _mmss(duration))
            c.drawString(margin, top - 32, line)
            c.setFillColor(colors.HexColor("#555555"))
            c.setFont("Helvetica", 8)
            how = "Read each column bottom to top, columns left to right. Lanes 1 to 4, left to right. Box = tap, long bar = hold."
            if not snapped:
                how += "  Tempo not steady: notes are not snapped to the beat, bar lines are a rough guide."
            c.drawString(margin, top - 45, how)
            top -= 58
        else:
            c.setFont("Helvetica", 9)
            c.setFillColor(colors.HexColor("#555555"))
            c.drawString(margin, top - 8, title + "  (chart)")
            top -= 18
        bottom = margin + 16
        height = top - bottom

        def y_of(t, col):
            return bottom + (t - (origin + col * col_time)) / col_time * height

        for k in range(COLS_PER_PAGE):
            col = page * COLS_PER_PAGE + k
            if col >= n_cols:
                break
            x0 = margin + k * col_w + gutter
            t0 = origin + col * col_time
            # lanes
            c.setFillColor(colors.HexColor("#f4f5f7"))
            c.rect(x0, bottom, strip_w, height, stroke=0, fill=1)
            c.setStrokeColor(colors.HexColor("#d0d3d8"))
            c.setLineWidth(0.4)
            for l in range(lanes + 1):
                c.line(x0 + l * lane_w, bottom, x0 + l * lane_w, top)
            # beat and bar lines
            for b in range(BARS_PER_COL * beats_per_bar + 1):
                y = bottom + b / (BARS_PER_COL * beats_per_bar) * height
                is_bar = b % beats_per_bar == 0
                c.setStrokeColor(colors.HexColor("#555555" if is_bar else "#c4c8ce"))
                c.setLineWidth(0.9 if is_bar else 0.4)
                c.line(x0, y, x0 + strip_w, y)
                if is_bar and b < BARS_PER_COL * beats_per_bar:
                    bar_no = col * BARS_PER_COL + b // beats_per_bar + 1
                    if bar_no <= n_bars:
                        c.setFillColor(colors.HexColor("#333333"))
                        c.setFont("Helvetica-Bold", 7)
                        c.drawRightString(x0 - 3, y + 1, str(bar_no))
            c.setFillColor(colors.HexColor("#777777"))
            c.setFont("Helvetica", 6.5)
            c.drawString(x0, bottom - 9, _mmss(t0))

            # notes
            t1 = t0 + col_time
            for n in notes:
                t = n["time_ms"] / 1000.0
                e = t + n["duration_ms"] / 1000.0
                if e < t0 or t >= t1:
                    continue
                lane = min(lanes - 1, max(0, n["lane"]))
                x = x0 + lane * lane_w
                col_color = colors.HexColor(LANE_COLORS[lane % len(LANE_COLORS)])
                if n["duration_ms"] > 0:
                    ya, yb = y_of(max(t, t0), col), y_of(min(e, t1), col)
                    c.setFillColor(col_color)
                    c.setFillAlpha(HOLD_ALPHA)
                    c.rect(x + lane_w * 0.25, ya, lane_w * 0.5, max(1.0, yb - ya), stroke=0, fill=1)
                    c.setFillAlpha(1)
                if t0 <= t < t1:
                    y = y_of(t, col)
                    c.setFillColor(col_color)
                    c.roundRect(x + 2, y - 2.2, lane_w - 4, 4.4, 1.5, stroke=0, fill=1)

        c.setFillColor(colors.HexColor("#777777"))
        c.setFont("Helvetica", 7)
        foot = "Page %d of %d" % (page + 1, n_pages)
        if source:
            foot = "Auto-generated from " + source + ". Check by playing it.    " + foot
        c.drawRightString(W - margin, margin - 12, foot)
        c.showPage()
    c.save()
    return n_pages
