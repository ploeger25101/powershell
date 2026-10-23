#!/usr/bin/env python3
"""Bitmap fonts for the standalone development host (tools/ProjectAbyss.Standalone).

In the real game the fonts come from Stardew Valley (Game1.smallFont / Game1.dialogueFont). This script renders
DejaVu Sans Bold into an atlas + JSON glyph table that the standalone host turns into MonoGame SpriteFonts.
"""
import json
import os
import sys
from PIL import Image, ImageDraw, ImageFont

FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
CHARS = "".join(sorted(set("".join(chr(c) for c in range(32, 127)) + "äöüÄÖÜß–·…✓−")))


def build(size, out_png, out_json):
    font = ImageFont.truetype(FONT, size)
    ascent, descent = font.getmetrics()
    line = ascent + descent
    cells = []
    x, y, row_h, width = 1, 1, 0, 512
    for ch in CHARS:
        bbox = font.getbbox(ch)
        w = max(1, bbox[2] - bbox[0])
        h = max(1, bbox[3] - bbox[1])
        if x + w + 1 > width:
            x = 1
            y += row_h + 1
            row_h = 0
        cells.append((ch, x, y, w, h, bbox[0], bbox[1], font.getlength(ch)))
        x += w + 1
        row_h = max(row_h, h)
    height = y + row_h + 2
    img = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    glyphs = []
    for ch, gx, gy, w, h, ox, oy, adv in cells:
        d.text((gx - ox, gy - oy), ch, font=font, fill=(255, 255, 255, 255))
        glyphs.append({"c": ord(ch), "x": gx, "y": gy, "w": w, "h": h, "ox": ox, "oy": oy, "adv": adv})
    img.save(out_png)
    with open(out_json, "w") as f:
        json.dump({"lineSpacing": line, "glyphs": glyphs}, f)


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out, exist_ok=True)
    build(20, os.path.join(out, "font_small.png"), os.path.join(out, "font_small.json"))
    build(30, os.path.join(out, "font_large.png"), os.path.join(out, "font_large.json"))
