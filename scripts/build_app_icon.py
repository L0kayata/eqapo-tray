"""Compose the app icon (the Fluent speaker_2 glyph above "EAT" in Noto Sans SC
Bold) and pack it into the exe's .ico.

Usage (from the repo root):
    python -m pip install resvg-py==0.5.0 fonttools==4.57.0 Pillow==11.2.1
    python scripts/build_app_icon.py [--font path/to/NotoSansSC-VariableFont_wght.ttf]

The letter outlines are read from icon/app/eat_glyphs.json, so no font needs
to be installed. --font re-extracts them first (Noto Sans SC, SIL OFL 1.1,
see icon/app/NotoSansSC-OFL.txt: https://fonts.google.com/noto/specimen/Noto+Sans+SC).

Outputs (overwritten on every run):
    src/EqApoTray/Assets/AppIcon.ico
        Multi-size .ico, one PNG-compressed entry per size. The exe's icon
        (<ApplicationIcon> in EqApoTray.csproj).
    icon/app/app_icon_{size}.svg
        Each size drawn at its own pixel size with the letters fitted to the
        pixel grid: speaker above "EAT" from 40 px up, "EAT" alone below.
    icon/app/png/app_icon_{size}.png
        The same rasters as loose files, for previewing / reuse.
"""

import argparse
import json
import math
import re
from io import BytesIO
from pathlib import Path

import resvg_py
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.recordingPen import RecordingPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.ttLib import TTFont
from PIL import Image

from build_icons import pack_ico

ROOT = Path(__file__).resolve().parent.parent
APP_DIR = ROOT / "icon" / "app"
PNG_DIR = APP_DIR / "png"
GLYPHS_JSON = APP_DIR / "eat_glyphs.json"
ICO_PATH = ROOT / "src" / "EqApoTray" / "Assets" / "AppIcon.ico"
SPEAKER_SVG = ROOT / "icon" / "ic_fluent_speaker_2_48_regular.svg"

TEXT = "EAT"
WEIGHT = 700
PLATE_TOP, PLATE_BOTTOM = "#3C9BF0", "#1660C8"
INK = "#FFFFFF"

# Plate corner radius in the 48 grid (scaled for every size).
RADIUS = 3

# Explorer, Task Manager and Settings pick from these: 16/32/48/256 at 100%,
# 20/24/40/64 cover the common scale factors. Others are scaled by Windows.
LARGE_SIZES = (40, 48, 64, 256)

# Small size -> (plate margin, cap height), px. Letters keep PAD px of plate
# on either side and are squeezed narrower (never shorter) to fit.
SMALL_LAYOUT = {16: (0, 6), 20: (0, 8), 24: (1, 8), 32: (1, 11)}
PAD = 1

# Speaker extents in its own 48 grid (measured from the path).
SPEAKER_BOX = (3.75, 6.0, 44.0, 42.0)

# Optical spacing: the white between two letters is averaged over the cap
# height, counting at most this deep (fraction of cap height) into a letter's
# open side, so E's counters and the space under T's bar don't count in full.
SPACING_DEPTH = 0.2

# Grid fitting: a pair of facing edges closer than this (fraction of cap
# height) is a stroke, snapped as a unit so it keeps a whole-pixel weight.
STROKE_MAX = 0.3


def rnd(v: float) -> int:
    return math.floor(v + 0.5)


def fmt(v: float) -> str:
    return f"{v:.3f}".rstrip("0").rstrip(".")


def extract_glyphs(font_path: Path) -> None:
    font = TTFont(font_path)
    glyph_set = font.getGlyphSet(location={"wght": WEIGHT})
    cmap = font.getBestCmap()
    name = font["name"]
    lines = []
    for c in TEXT:
        glyph = glyph_set[cmap[ord(c)]]
        pen = RecordingPen()
        glyph.draw(pen)
        outline = [[op, [[round(x, 3), round(y, 3)] for x, y in pts]] for op, pts in pen.value]
        lines.append(f"  {json.dumps(c)}: {json.dumps({'advance': round(glyph.width, 3), 'outline': outline})}")
    header = {
        "font": f"Noto Sans SC, wght {WEIGHT} ({name.getDebugName(5)})",
        "copyright": name.getDebugName(0),
        "license": "SIL Open Font License 1.1, see NotoSansSC-OFL.txt",
    }
    meta = "".join(f"{json.dumps(k)}: {json.dumps(v)},\n" for k, v in header.items())
    body = ",\n".join(lines)
    GLYPHS_JSON.write_text(f'{{\n{meta}"glyphs": {{\n{body}\n}}\n}}\n', encoding="utf-8")


def straight_edges(outline) -> tuple[dict, dict, list]:
    """Coordinates of the vertical (x) and horizontal (y) line segments, each
    tagged "lo" (ink on its high side: a stroke's left / bottom edge) or "hi",
    plus every horizontal segment as (y, x_left, x_right). TrueType contours
    run clockwise, so ink lies right of the direction of travel."""
    x_edges, y_edges, flats = {}, {}, []

    def segment(p, q):
        if p[1] == q[1] and p[0] != q[0]:
            y_edges.setdefault(p[1], set()).add("hi" if q[0] > p[0] else "lo")
            flats.append((p[1], min(p[0], q[0]), max(p[0], q[0])))
        elif p[0] == q[0] and p[1] != q[1]:
            x_edges.setdefault(p[0], set()).add("lo" if q[1] > p[1] else "hi")

    start = cur = None
    for op, pts in outline:
        if op == "moveTo":
            start = cur = pts[0]
        elif op == "lineTo":
            segment(cur, pts[0])
            cur = pts[0]
        elif op == "closePath":
            segment(cur, start)
            cur = start
        else:
            cur = pts[-1]
    return x_edges, y_edges, flats


def strokes(edges: dict, max_width: float) -> tuple[list, list]:
    """Split edges into strokes (a "lo" edge followed by a "hi" edge within
    max_width, as (a, b)) and the remaining single edges."""
    coords = sorted(edges)
    spans, singles = [], []
    i = 0
    while i < len(coords):
        a = coords[i]
        if i + 1 < len(coords):
            b = coords[i + 1]
            if edges[a] == {"lo"} and edges[b] == {"hi"} and b - a < max_width:
                spans.append((a, b))
                i += 2
                continue
        singles.append(a)
        i += 1
    return spans, singles


def fit_axis(spans, singles, lo: float, hi: float, out_lo: int, out_hi: int, k: float, max_width: float):
    """Piecewise-linear map of one axis taking lo/hi to out_lo/out_hi, with every
    edge on a whole pixel. A span narrower than max_width is a stroke and stays
    round(width * k) >= 1 px thick; on the centre line it moves a pixel thinner
    or thicker, whichever is closer, if needed to stay centred (A's apex)."""
    scale = (out_hi - out_lo) / (hi - lo)
    keys = {lo: out_lo, hi: out_hi}
    for a in singles:
        keys.setdefault(a, rnd(out_lo + (a - lo) * scale))
    for a, b in spans:
        narrow = b - a < max_width
        exact = (b - a) * (k if narrow else scale)
        t = max(1, rnd(exact))
        if a == lo:
            s = out_lo
        elif b == hi:
            s = out_hi - t
        else:
            if narrow and abs((a + b) / 2 - (lo + hi) / 2) < 0.01 * (hi - lo) and (out_hi - out_lo - t) % 2:
                t = min((c for c in (t - 1, t + 1) if c >= 1), key=lambda c: abs(c - exact))
            s = rnd(out_lo + ((a + b) / 2 - lo) * scale - t / 2)
        keys[a], keys[b] = s, s + t
    src = sorted(keys)
    dst = [keys[v] for v in src]
    for i in range(1, len(dst)):
        dst[i] = max(dst[i], dst[i - 1])

    def f(v: float) -> float:
        if v <= src[0]:
            return dst[0] + (v - src[0]) * scale
        for i in range(1, len(src)):
            if v <= src[i]:
                return dst[i - 1] + (v - src[i - 1]) * (dst[i] - dst[i - 1]) / (src[i] - src[i - 1])
        return dst[-1] + (v - src[-1]) * scale

    return f


class Glyph:
    def __init__(self, advance: float, outline: list):
        self.advance = advance
        self.outline = outline
        pen = BoundsPen(None)
        self.draw(pen)
        self.xmin, _, self.xmax, self.ymax = pen.bounds
        self.x_edges, self.y_edges, self.flats = straight_edges(outline)

    @property
    def ink_width(self) -> float:
        return self.xmax - self.xmin

    def draw(self, pen, fx=lambda x: x, fy=lambda y: y) -> None:
        for op, pts in self.outline:
            getattr(pen, op)(*[(fx(x), fy(y)) for x, y in pts])

    def centered_stem(self, max_width: float) -> float | None:
        """Width of a vertical stroke on the glyph's centre line (T), if any."""
        mid = (self.xmin + self.xmax) / 2
        for a, b in strokes(self.x_edges, max_width)[0]:
            if abs((a + b) / 2 - mid) < 0.01 * self.ink_width:
                return b - a
        return None

    def fit(self, width: int, cap: int, cap_units: float, max_width: float):
        """Grid-fitting point maps (fx, fy): ink box to [0, width] x [0, cap] px.
        x keys are the vertical stems plus the extents of whatever sits on the
        baseline or cap line (bars, feet, A's apex); y keys every horizontal edge."""
        k = cap / cap_units
        stems, singles = strokes(self.x_edges, max_width)
        on_lines = [(l, r) for y, l, r in self.flats if y in (0, cap_units)]
        fx = fit_axis(stems + on_lines, singles, self.xmin, self.xmax, 0, width, k, max_width)
        fy = fit_axis(*strokes(self.y_edges, max_width), 0, cap_units, 0, cap, k, max_width)
        return fx, fy

    def path(self, fx, fy) -> str:
        """SVG path data with every point mapped through fx / fy."""
        pen = SVGPathPen(None, ntos=fmt)
        self.draw(pen, fx, fy)
        return pen.getCommands()


def load_glyphs() -> list[Glyph]:
    data = json.loads(GLYPHS_JSON.read_text(encoding="utf-8"))["glyphs"]
    return [
        Glyph(data[c]["advance"], [(op, [tuple(p) for p in pts]) for op, pts in data[c]["outline"]]) for c in TEXT
    ]


def render(svg: str, width: int, height: int | None = None) -> bytes:
    png = resvg_py.svg_to_bytes(svg_string=svg, width=width, height=height or width, skip_system_fonts=True)
    return bytes(png)


def ink_rows(glyph: Glyph, cap: float) -> list[tuple[int, int]]:
    """Leftmost / rightmost ink column per row of the cap band, 1 px = 1 font unit."""
    width, height = math.ceil(glyph.ink_width), math.ceil(cap)
    d = glyph.path(lambda x: x - glyph.xmin, lambda y: cap - y)
    svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}"><path d="{d}"/></svg>'
    alpha = Image.open(BytesIO(render(svg, width, height))).getchannel("A").point(lambda a: 255 if a >= 128 else 0)
    rows = []
    for y in range(height):
        box = alpha.crop((0, y, width, y + 1)).getbbox()
        if box:
            rows.append((box[0], box[2]))
    return rows


def optical_gaps(glyphs: list[Glyph], cap: float) -> list[float]:
    """Ink-to-ink gap after each letter but the last (font units), chosen so every
    pair has the same optical gap: the one the font's sidebearings give the first."""
    depth = SPACING_DEPTH * cap
    rows = [ink_rows(g, cap) for g in glyphs]
    right_depth = [sum(min(r[-1][1] - right, depth) for _, right in r) / len(r) for r in rows]
    left_depth = [sum(min(left, depth) for left, _ in r) / len(r) for r in rows]
    first, second = glyphs[0], glyphs[1]
    target = (first.advance - first.xmax) + second.xmin + right_depth[0] + left_depth[1]
    return [target - right_depth[i] - left_depth[i + 1] for i in range(len(glyphs) - 1)]


def fit_widths(glyphs: list[Glyph], k: float, budget: int, max_stroke: float) -> list[int]:
    """Whole-pixel letter widths, a centred stem kept centred, squeezed into budget."""
    widths, steps = [], []
    for g in glyphs:
        natural = g.ink_width * k
        stem = g.centered_stem(max_stroke)
        if stem is None:
            widths.append(rnd(natural))
            steps.append(1)
        else:
            t = max(1, rnd(stem * k))
            widths.append(t + 2 * rnd((natural - t) / 2))
            steps.append(2)
    while sum(widths) > budget:
        i = min(range(len(glyphs)), key=lambda i: abs((widths[i] - steps[i]) / (glyphs[i].ink_width * k) - 1))
        widths[i] -= steps[i]
    return widths


def fitted_text(glyphs: list[Glyph], gaps: list[float], cap_units: float, cap: int, size: int, budget: int, baseline: int) -> str:
    """The letters centred across the icon, every straight edge on the pixel grid."""
    k = cap / cap_units
    max_stroke = STROKE_MAX * cap_units
    gaps_px = [max(1, rnd(g * k)) for g in gaps]
    widths = fit_widths(glyphs, k, budget - sum(gaps_px), max_stroke)
    left = math.ceil((size - sum(widths) - sum(gaps_px)) / 2)
    text = ""
    for g, width, after in zip(glyphs, widths, gaps_px + [0]):
        fx, fy = g.fit(width, cap, cap_units, max_stroke)
        text += ink(g.path(lambda x, fx=fx, left=left: left + fx(x), lambda y, fy=fy: baseline - fy(y)))
        left += width + after
    return text


def plate(size: float, margin: float, radius: float) -> str:
    side = size - 2 * margin
    return (
        '<defs><linearGradient id="plate" x1="0" y1="0" x2="0.4" y2="1">'
        f'<stop offset="0" stop-color="{PLATE_TOP}"/><stop offset="1" stop-color="{PLATE_BOTTOM}"/>'
        "</linearGradient></defs>"
        f'<rect x="{margin}" y="{margin}" width="{side}" height="{side}" rx="{fmt(radius)}" fill="url(#plate)"/>'
    )


def svg_doc(size: int, body: str) -> str:
    return f'<svg width="{size}" height="{size}" viewBox="0 0 {size} {size}" xmlns="http://www.w3.org/2000/svg">{body}</svg>\n'


def ink(d: str) -> str:
    return f'<path d="{d}" fill="{INK}"/>'


def large_svg(size: int, speaker_d: str, glyphs: list[Glyph], gaps: list[float], cap_units: float) -> str:
    """Speaker above "EAT", laid out on the 48 grid and drawn at its own pixel size."""
    u = size / 48
    s = 0.62 * u
    cap = rnd(8.2 * u)
    x0, y0, x1, y1 = SPEAKER_BOX
    speaker_h = (y1 - y0) * s
    gap = 3 * u
    baseline = rnd((size + speaker_h + gap + cap) / 2)
    top = baseline - cap - gap - speaker_h
    speaker = (
        f'<path transform="translate({fmt(size / 2 - (x0 + x1) / 2 * s)} {fmt(top - y0 * s)}) '
        f'scale({fmt(s)})" d="{speaker_d}" fill="{INK}"/>'
    )
    text = fitted_text(glyphs, gaps, cap_units, cap, size, size, baseline)
    return svg_doc(size, plate(size, rnd(2 * u), RADIUS * u) + speaker + text)


def small_svg(size: int, glyphs: list[Glyph], gaps: list[float], cap_units: float) -> str:
    """"EAT" alone, as large as the plate allows."""
    margin, cap = SMALL_LAYOUT[size]
    baseline = (size - cap) // 2 + cap
    text = fitted_text(glyphs, gaps, cap_units, cap, size, size - 2 * (margin + PAD), baseline)
    return svg_doc(size, plate(size, margin, size * RADIUS / 48) + text)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--font", type=Path, help="re-extract the letters from this Noto Sans SC variable font first")
    args = parser.parse_args()
    if args.font:
        extract_glyphs(args.font)
        print(f"{GLYPHS_JSON.relative_to(ROOT)}  (from {args.font})")

    glyphs = load_glyphs()
    # Measured, not OS/2 sCapHeight: that is the default (Thin) master's.
    cap = max(g.ymax for g in glyphs)
    gaps = optical_gaps(glyphs, cap)
    speaker_d = re.search(r' d="([^"]+)"', SPEAKER_SVG.read_text(encoding="utf-8")).group(1)

    svgs = {s: small_svg(s, glyphs, gaps, cap) for s in SMALL_LAYOUT}
    svgs |= {s: large_svg(s, speaker_d, glyphs, gaps, cap) for s in LARGE_SIZES}
    PNG_DIR.mkdir(parents=True, exist_ok=True)
    images = []
    for size in sorted(svgs):
        (APP_DIR / f"app_icon_{size}.svg").write_text(svgs[size], encoding="utf-8")
        png = render(svgs[size], size)
        (PNG_DIR / f"app_icon_{size}.png").write_bytes(png)
        images.append((size, png))
    ICO_PATH.write_bytes(pack_ico(images))
    print(f"{ICO_PATH.relative_to(ROOT)}  ({len(images)} sizes, {ICO_PATH.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
