#!/usr/bin/env python3
"""Rasterises assets/ModIcon.svg to Resources/About/ModIcon.png.

The geometry is redrawn here rather than handed to an SVG rasteriser because
the design depends on <mask>, and cairosvg ignores mask entirely - it renders
both dice as solid rounded squares with no pips. The masks are the design, so
silently losing them is worse than not rendering at all.

Everything below mirrors assets/ModIcon.svg one-for-one. Change the SVG and
change this, or the committed PNG stops matching its source.

    python3 Tools/render-icon.py [--review]
"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent

VIEWBOX = 32
SUPERSAMPLE = 16  # drawn at 512, reduced to 32 with LANCZOS

FRONT = (0xE5, 0xDD, 0xD0, 0xFF)
BACK = (0xC8, 0xA0, 0x5A, 0xFF)

# x, y, w, h, radius  - the two <rect> elements
BACK_DIE = (14, 0.5, 17.5, 17.5, 4.5)
FRONT_DIE = (0.5, 12, 19.5, 19.5, 5)

# The front die's silhouette, grown, punched out of the back die: this is what
# makes the gold one read as a separate object behind rather than a smear.
OCCLUDER = (-2, 9.5, 25, 25, 7.5)

# Five pips as a plus, centred on each die's face.
BACK_PIPS = [(22.75, 9.25), (22.75, 4.5), (22.75, 14), (18, 9.25), (27.5, 9.25)]
BACK_PIP_R = 1.6
FRONT_PIPS = [(10.25, 21.75), (10.25, 16.75), (10.25, 26.75), (5.25, 21.75), (15.25, 21.75)]
FRONT_PIP_R = 1.7


def layer(size):
    return Image.new("RGBA", (size, size), (0, 0, 0, 0))


def rounded(draw, rect, fill, s):
    x, y, w, h, r = rect
    draw.rounded_rectangle([x * s, y * s, (x + w) * s, (y + h) * s], radius=r * s, fill=fill)


def punch_rounded(im, rect, s):
    """SVG masks are luminance-keyed; black hides. Here that is alpha 0."""
    hole = layer(im.size[0])
    rounded(ImageDraw.Draw(hole), rect, (0, 0, 0, 255), s)
    im.putalpha(Image.fromarray(
        (_alpha(im) * (255 - _alpha(hole)) // 255).astype("uint8")))


def punch_circles(im, centres, radius, s):
    hole = layer(im.size[0])
    d = ImageDraw.Draw(hole)
    for cx, cy in centres:
        d.ellipse([(cx - radius) * s, (cy - radius) * s,
                   (cx + radius) * s, (cy + radius) * s], fill=(0, 0, 0, 255))
    im.putalpha(Image.fromarray(
        (_alpha(im) * (255 - _alpha(hole)) // 255).astype("uint8")))


def _alpha(im):
    import numpy
    return numpy.array(im.split()[3], dtype="uint16")


def render(size):
    s = size * SUPERSAMPLE / VIEWBOX
    big = size * SUPERSAMPLE

    back = layer(big)
    rounded(ImageDraw.Draw(back), BACK_DIE, BACK, s)
    punch_rounded(back, OCCLUDER, s)
    punch_circles(back, BACK_PIPS, BACK_PIP_R, s)

    front = layer(big)
    rounded(ImageDraw.Draw(front), FRONT_DIE, FRONT, s)
    punch_circles(front, FRONT_PIPS, FRONT_PIP_R, s)

    return Image.alpha_composite(back, front).resize((size, size), Image.LANCZOS)


def main():
    icon = render(32)
    out = ROOT / "Resources" / "About" / "ModIcon.png"
    icon.save(out)
    print(f"{out.relative_to(ROOT)}  32x32 RGBA")

    for size in (256, 64):
        p = ROOT / "assets" / f"ModIcon-{size}.png"
        render(size).save(p)
        print(f"{p.relative_to(ROOT)}  {size}x{size} RGBA")

    if "--review" in sys.argv:
        dark = (38, 36, 33, 255)
        sheet = Image.new("RGBA", (424, 500), dark)
        blow = icon.resize((384, 384), Image.NEAREST)
        sheet.paste(blow, (20, 20), blow)
        for x in range(20, 20 + 32 * 5, 44):
            sheet.paste(icon, (x, 430), icon)
        p = ROOT / "assets" / "icon-review.png"
        sheet.convert("RGB").save(p)
        print(f"{p.relative_to(ROOT)}  12x blowup over the mod-list row colour")


if __name__ == "__main__":
    main()
