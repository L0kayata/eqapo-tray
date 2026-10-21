"""Rasterize the Fluent speaker SVGs in icon/ into the tray icon assets.

Usage (from the repo root):
    python -m pip install resvg-py==0.5.0
    python scripts/build_icons.py

Outputs (overwritten on every run):
    src/EqApoTray/Assets/TrayIcons/speaker_{level}_{theme}.ico
        Multi-size .ico, one PNG-compressed entry per size in SIZES. Embedded
        into the exe and loaded by Services/TrayIconSwitcher.cs.
    icon/png/{theme}/speaker_{level}_{size}.png
        The same rasters as loose files, for previewing / reuse.

"light" / "dark" name the *taskbar* theme an icon is meant for: the light
variant is a dark glyph (for a light taskbar), the dark variant a white one.
"""

import struct
from pathlib import Path

import resvg_py

ROOT = Path(__file__).resolve().parent.parent
SVG_DIR = ROOT / "icon"
ICO_DIR = ROOT / "src" / "EqApoTray" / "Assets" / "TrayIcons"
PNG_DIR = ROOT / "icon" / "png"

LEVELS = (0, 1, 2)

# Fluent ships each glyph pre-hinted at 16/20/24/28/32/48, but the 20 and 24
# designs draw noticeably shorter waves than the rest, so they are not used.
SOURCE_SIZES = (16, 28, 32, 48)

# SM_CXSMICON (the tray icon size) at 100%..300% scaling. Rarer scales get
# the 48 entry, scaled by Windows.
SIZES = (16, 20, 24, 28, 32, 36, 40, 48)

# Target size -> design size to render it from. Exact or integer multiples
# keep the hinted strokes on the pixel grid. 20 and 24 come from 32: scaling
# the 16 design up merges its waves at 20. Sizes with no clean source take
# the most detailed (48) design.
SOURCE_FOR = {16: 16, 20: 32, 24: 32, 28: 28, 32: 32, 36: 48, 40: 48, 48: 48}

SOURCE_FILL = 'fill="#212121"'
THEME_FILLS = {
    "light": SOURCE_FILL,
    "dark": 'fill="#FFFFFF"',
}


def load_svg(level: int, design_size: int, theme: str) -> str:
    path = SVG_DIR / f"ic_fluent_speaker_{level}_{design_size}_regular.svg"
    svg = path.read_text(encoding="utf-8")
    if SOURCE_FILL not in svg:
        raise SystemExit(f"{path.name}: expected {SOURCE_FILL}, cannot recolor")
    return svg.replace(SOURCE_FILL, THEME_FILLS[theme])


def render(svg: str, size: int) -> bytes:
    return resvg_py.svg_to_bytes(svg_string=svg, width=size, height=size, skip_system_fonts=True)


def pack_ico(images: list[tuple[int, bytes]]) -> bytes:
    # ICONDIR + one 16-byte ICONDIRENTRY per image, then the PNG payloads.
    # The entry's width/height bytes store 256 as 0.
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = len(header) + 16 * len(images)
    entries = b""
    payload = b""
    for size, png in images:
        entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(png), offset)
        payload += png
        offset += len(png)
    return header + entries + payload


def main() -> None:
    assert set(SOURCE_FOR) == set(SIZES) and set(SOURCE_FOR.values()) <= set(SOURCE_SIZES)

    ICO_DIR.mkdir(parents=True, exist_ok=True)
    for theme in THEME_FILLS:
        png_dir = PNG_DIR / theme
        png_dir.mkdir(parents=True, exist_ok=True)
        for level in LEVELS:
            svgs = {s: load_svg(level, s, theme) for s in set(SOURCE_FOR.values())}
            images = [(size, render(svgs[SOURCE_FOR[size]], size)) for size in SIZES]
            for size, png in images:
                (png_dir / f"speaker_{level}_{size}.png").write_bytes(png)
            ico = ICO_DIR / f"speaker_{level}_{theme}.ico"
            ico.write_bytes(pack_ico(images))
            print(f"{ico.relative_to(ROOT)}  ({len(images)} sizes, {ico.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
