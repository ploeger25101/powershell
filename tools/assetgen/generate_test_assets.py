#!/usr/bin/env python3
"""Generate Project Abyss *test* pixel art (original, procedurally drawn placeholders).

Output goes to src/ProjectAbyss.Smapi/assets. These are development assets for the vertical slice; the
final hand-made art (design phase 7) replaces them using the same atlas metadata format (sprites.json).

Usage: python3 tools/assetgen/generate_test_assets.py
"""
import json
import math
import os
from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "src", "ProjectAbyss.Smapi", "assets")

# Direction8 order used by the game: Up, UpRight, Right, DownRight, Down, DownLeft, Left, UpLeft
DIRS = ["up", "up_right", "right", "down_right", "down", "down_left", "left", "up_left"]
DIR_VEC = [(0, -1), (0.707, -0.707), (1, 0), (0.707, 0.707), (0, 1), (-0.707, 0.707), (-1, 0), (-0.707, -0.707)]

OUTLINE = (16, 12, 22, 255)
STEEL = [(46, 50, 64, 255), (78, 84, 104, 255), (122, 130, 152, 255), (176, 184, 204, 255)]
CRIMSON = [(58, 12, 22, 255), (104, 22, 36, 255), (150, 38, 50, 255)]
LEATHER = [(44, 30, 24, 255), (74, 52, 38, 255)]
SKIN = [(128, 92, 80, 255), (170, 128, 110, 255)]
EYE = (230, 200, 120, 255)

# animation name -> (frames, fps, loop)
PLAYER_ANIMS = [
    ("idle", 4, 6, True),
    ("walk", 6, 10, True),
    ("attack", 4, 14, False),
    ("special", 4, 12, False),
    ("dash", 3, 14, False),
    ("ability", 4, 12, False),
    ("ultimate", 4, 10, False),
    ("hit", 2, 10, False),
    ("downed", 2, 3, True),
    ("revive", 3, 6, False),
]
PW, PH = 48, 64
MAXF = 6


def px_rect(d, x0, y0, x1, y1, fill, outline=OUTLINE):
    d.rectangle([min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)], fill=fill, outline=outline)


def draw_warrior(img, ox, oy, dir_index, anim, frame, nframes):
    d = ImageDraw.Draw(img)
    vx, vy = DIR_VEC[dir_index]
    t = frame / max(1, nframes)
    bob = 0
    leg_phase = 0.0
    lean = 0
    crouch = 0
    if anim == "idle":
        bob = 1 if frame in (1, 2) else 0
    elif anim == "walk":
        leg_phase = math.sin(t * 2 * math.pi)
        bob = 1 if frame % 3 == 1 else 0
    elif anim in ("attack", "special", "ability", "ultimate"):
        lean = [0, 2, 4, 2][frame % 4]
    elif anim == "dash":
        lean = 5
        crouch = 3
    elif anim == "hit":
        lean = -3
    elif anim == "downed":
        crouch = 18
    elif anim == "revive":
        crouch = [16, 8, 2][frame % 3]

    cx = ox + PW // 2 + int(vx * lean)
    base = oy + 58
    # shadow
    d.ellipse([ox + 12, base - 3, ox + 36, base + 3], fill=(0, 0, 0, 90))
    if anim == "downed":
        # lying body
        px_rect(d, cx - 14, base - 12, cx + 14, base - 4, STEEL[1])
        px_rect(d, cx + 10, base - 14, cx + 18, base - 6, STEEL[2])
        d.rectangle([cx - 16, base - 8, cx - 4, base - 3], fill=CRIMSON[1], outline=OUTLINE)
        return
    top = base - 50 + bob + crouch
    facing_up = vy < -0.3
    # cloak behind body unless facing up
    cloak_col = CRIMSON[1] if not facing_up else CRIMSON[0]
    if not facing_up:
        d.polygon([(cx - 11, top + 18), (cx + 11, top + 18), (cx + 14 - int(vx * 6), base - 6), (cx - 14 - int(vx * 6), base - 6)], fill=CRIMSON[0], outline=OUTLINE)
    # legs
    l1 = int(leg_phase * 4)
    px_rect(d, cx - 7 + int(vx * 1), top + 34 + max(0, -l1) // 2, cx - 2, base - 2 - max(0, l1), LEATHER[0])
    px_rect(d, cx + 2, top + 34 + max(0, l1) // 2, cx + 7 + int(vx * 1), base - 2 - max(0, -l1), LEATHER[0])
    px_rect(d, cx - 8, base - 5 - max(0, l1), cx - 1, base - 1 - max(0, l1), STEEL[0])
    px_rect(d, cx + 1, base - 5 - max(0, -l1), cx + 8, base - 1 - max(0, -l1), STEEL[0])
    # torso (breastplate)
    px_rect(d, cx - 10, top + 16, cx + 10, top + 36, STEEL[1])
    d.rectangle([cx - 8, top + 18, cx + 8 - int(abs(vx) * 4), top + 24], fill=STEEL[2])
    d.line([cx - 10, top + 30, cx + 10, top + 30], fill=LEATHER[1], width=2)
    # tabard showing class colour
    if not facing_up:
        d.rectangle([cx - 4 + int(vx * 3), top + 24, cx + 4 + int(vx * 3), top + 38], fill=CRIMSON[2], outline=OUTLINE)
    # pauldrons
    px_rect(d, cx - 14, top + 14, cx - 7, top + 22, STEEL[2])
    px_rect(d, cx + 7, top + 14, cx + 14, top + 22, STEEL[2])
    # head / helmet
    hx = cx + int(vx * 2)
    px_rect(d, hx - 8, top + 1, hx + 8, top + 16, STEEL[1])
    d.rectangle([hx - 6, top + 2, hx + 6, top + 5], fill=STEEL[3])
    if not facing_up:
        # visor slit oriented to the facing direction
        sx = hx + int(vx * 4)
        d.rectangle([sx - 5 + (2 if vx > 0.3 else 0), top + 8, sx + 5 - (2 if vx < -0.3 else 0), top + 10], fill=OUTLINE)
        d.point([sx - 2 + int(vx * 2), top + 9], fill=EYE)
        d.point([sx + 2 + int(vx * 2), top + 9], fill=EYE)
    else:
        d.rectangle([hx - 1, top + 2, hx + 1, top + 15], fill=STEEL[2])
    # crest
    d.rectangle([hx - 1, top - 3, hx + 1, top + 1], fill=CRIMSON[2], outline=OUTLINE)
    # arms: weapon arm extends towards facing during attacks
    reach = {"attack": [2, 6, 10, 6], "special": [0, 4, 8, 8], "ability": [4, 8, 8, 4], "ultimate": [0, 2, 10, 10]}.get(anim, [0, 0, 0, 0])[frame % 4] if anim in ("attack", "special", "ability", "ultimate") else 0
    ax = cx + int(vx * (10 + reach))
    ay = top + 24 + int(vy * (4 + reach // 2))
    px_rect(d, ax - 3, ay - 3, ax + 3, ay + 3, STEEL[2])
    # shield on the off-hand side
    sx2 = cx - int(vx * 9) - (6 if abs(vx) < 0.3 else 0)
    if vy > -0.3:
        d.rectangle([sx2 - 5, top + 20, sx2 + 4, top + 32], fill=CRIMSON[1], outline=OUTLINE)
        d.point([sx2, top + 26], fill=STEEL[3])
    if anim == "hit":
        d.rectangle([cx - 10, top + 16, cx + 10, top + 36], outline=(240, 240, 240, 255))
    if anim == "ultimate" and frame >= 2:
        d.ellipse([cx - 20, top - 6, cx + 20, base], outline=(230, 60, 60, 160))


def make_player_sheet():
    img = Image.new("RGBA", (PW * MAXF * 8, PH * len(PLAYER_ANIMS)), (0, 0, 0, 0))
    meta = {"frameWidth": PW, "frameHeight": PH, "originX": 24, "originY": 58, "framesPerRowPerDirection": MAXF, "directions": DIRS, "animations": {}}
    for row, (name, nframes, fps, loop) in enumerate(PLAYER_ANIMS):
        meta["animations"][name] = {"row": row, "frames": nframes, "fps": fps, "loop": loop}
        for di in range(8):
            for f in range(nframes):
                draw_warrior(img, (di * MAXF + f) * PW, row * PH, di, name, f, nframes)
    img.save(os.path.join(OUT, "characters", "warrior.png"))
    return meta


def make_weapons():
    # each weapon points right (+X) with its grip at (6, 16) so the renderer can rotate it to the exact aim angle
    img = Image.new("RGBA", (32 * 3, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # sword
    d.rectangle([2, 15, 8, 17], fill=LEATHER[1], outline=OUTLINE)
    d.rectangle([8, 12, 10, 20], fill=STEEL[2], outline=OUTLINE)
    d.polygon([(10, 14), (28, 15), (30, 16), (28, 17), (10, 18)], fill=STEEL[3], outline=OUTLINE)
    # axe
    o = 32
    d.rectangle([o + 2, 15, o + 22, 17], fill=LEATHER[1], outline=OUTLINE)
    d.polygon([(o + 18, 8), (o + 28, 6), (o + 30, 16), (o + 28, 26), (o + 18, 24), (o + 20, 16)], fill=STEEL[2], outline=OUTLINE)
    d.line([(o + 28, 8), (o + 29, 24)], fill=STEEL[3])
    # mace
    o = 64
    d.rectangle([o + 2, 15, o + 20, 17], fill=LEATHER[1], outline=OUTLINE)
    d.ellipse([o + 18, 9, o + 30, 23], fill=STEEL[1], outline=OUTLINE)
    for sx, sy in [(o + 24, 7), (o + 31, 16), (o + 24, 25), (o + 17, 16)]:
        d.rectangle([sx - 1, sy - 1, sx + 1, sy + 1], fill=STEEL[3], outline=OUTLINE)
    img.save(os.path.join(OUT, "characters", "weapons.png"))
    return {"frameWidth": 32, "frameHeight": 32, "gripX": 6, "gripY": 16, "order": ["warrior_sword", "warrior_axe", "warrior_mace"]}


def make_enemy(name, size, body, accent, kind):
    frames = 6  # 0-3 walk, 4-5 attack
    img = Image.new("RGBA", (size * frames, size), (0, 0, 0, 0))
    for f in range(frames):
        d = ImageDraw.Draw(img)
        ox = f * size
        c = size // 2
        bob = 1 if f in (1, 3) else 0
        atk = f >= 4
        d.ellipse([ox + c - size // 3, size - 5, ox + c + size // 3, size - 1], fill=(0, 0, 0, 90))
        if kind == "ghoul":
            d.ellipse([ox + c - 9, 9 + bob, ox + c + 9, size - 4], fill=body[0], outline=OUTLINE)
            d.ellipse([ox + c - 7, 3 + bob, ox + c + 7, 15 + bob], fill=body[1], outline=OUTLINE)
            d.point([ox + c + 3, 8 + bob], fill=accent)
            d.point([ox + c + 5, 8 + bob], fill=accent)
            claw = 6 if atk else 0
            d.line([ox + c + 8, 16, ox + c + 12 + claw, 20 - claw // 2], fill=body[1], width=2)
            d.line([ox + c - 8, 16, ox + c - 12, 20], fill=body[1], width=2)
        elif kind == "archer":
            d.polygon([(ox + c, 3 + bob), (ox + c + 8, size - 5), (ox + c - 8, size - 5)], fill=body[0], outline=OUTLINE)
            d.ellipse([ox + c - 5, 4 + bob, ox + c + 5, 13 + bob], fill=body[1], outline=OUTLINE)
            d.point([ox + c + 2, 8 + bob], fill=accent)
            draw = 3 if atk else 0
            d.arc([ox + c + 4 - draw, 8, ox + c + 14, size - 8], 270, 90, fill=(120, 80, 40, 255), width=2)
            d.line([ox + c + 9 - draw, 9, ox + c + 9 - draw, size - 9], fill=(200, 200, 180, 255))
        elif kind == "shaman":
            d.polygon([(ox + c, 6 + bob), (ox + c + 10, size - 4), (ox + c - 10, size - 4)], fill=body[0], outline=OUTLINE)
            d.ellipse([ox + c - 6, 2 + bob, ox + c + 6, 14 + bob], fill=body[1], outline=OUTLINE)
            d.point([ox + c + 2, 8 + bob], fill=accent)
            d.line([ox + c - 9, 6, ox + c - 9, size - 4], fill=(90, 60, 40, 255), width=2)
            glow = (140, 220, 90, 255) if atk else (90, 140, 60, 255)
            d.ellipse([ox + c - 12, 2, ox + c - 6, 8], fill=glow, outline=OUTLINE)
        elif kind == "sprout":
            d.ellipse([ox + c - 6, 8 + bob, ox + c + 6, size - 3], fill=body[0], outline=OUTLINE)
            for k in range(3):
                d.line([ox + c - 4 + k * 4, 9 + bob, ox + c - 6 + k * 6, 2 + bob], fill=body[1], width=1)
            d.point([ox + c + 2, 12 + bob], fill=accent)
    img.save(os.path.join(OUT, "enemies", f"{name}.png"))
    return {"frameWidth": size, "frameHeight": size, "originX": size // 2, "originY": size - 3, "walkFrames": 4, "attackFrames": 2, "faceRight": True}


def make_boss():
    size = 96
    frames = 6
    img = Image.new("RGBA", (size * frames, size), (0, 0, 0, 0))
    bark = [(36, 28, 22, 255), (62, 46, 34, 255), (92, 70, 50, 255)]
    moss = [(34, 58, 30, 255), (58, 92, 44, 255)]
    for f in range(frames):
        d = ImageDraw.Draw(img)
        ox = f * size
        sway = [0, 1, 2, 1, -2, 4][f]
        d.ellipse([ox + 18, size - 12, ox + 78, size - 2], fill=(0, 0, 0, 100))
        d.polygon([(ox + 30, size - 8), (ox + 66, size - 8), (ox + 60 + sway, 30), (ox + 36 + sway, 30)], fill=bark[1], outline=OUTLINE)
        for k in range(4):
            d.line([ox + 36 + k * 7, 34, ox + 34 + k * 8, size - 10], fill=bark[0])
        d.ellipse([ox + 26 + sway, 8, ox + 70 + sway, 44], fill=moss[0], outline=OUTLINE)
        d.ellipse([ox + 34 + sway, 12, ox + 62 + sway, 30], fill=moss[1])
        d.rectangle([ox + 40 + sway, 32, ox + 44 + sway, 36], fill=(240, 120, 40, 255))
        d.rectangle([ox + 52 + sway, 32, ox + 56 + sway, 36], fill=(240, 120, 40, 255))
        arm = 10 if f >= 4 else 0
        d.line([ox + 32, 44, ox + 12 - arm, 70 + arm], fill=bark[2], width=6)
        d.line([ox + 64, 44, ox + 84 + arm, 70 + arm], fill=bark[2], width=6)
        d.line([ox + 32, 44, ox + 12 - arm, 70 + arm], fill=OUTLINE, width=1)
        d.line([ox + 64, 44, ox + 84 + arm, 70 + arm], fill=OUTLINE, width=1)
    img.save(os.path.join(OUT, "bosses", "forest_warden.png"))
    return {"frameWidth": size, "frameHeight": size, "originX": 48, "originY": 90, "walkFrames": 4, "attackFrames": 2, "faceRight": True}


def make_tiles():
    names = ["floor0", "floor1", "floor2", "floor3", "wall", "obstacle", "hazard", "exit"]
    img = Image.new("RGBA", (16 * len(names), 16), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    import random
    rnd = random.Random(7)
    for i in range(4):
        ox = i * 16
        d.rectangle([ox, 0, ox + 15, 15], fill=(26, 32, 26, 255))
        for _ in range(10):
            x, y = rnd.randrange(16), rnd.randrange(16)
            d.point([ox + x, y], fill=rnd.choice([(34, 44, 32, 255), (20, 24, 20, 255), (40, 52, 36, 255)]))
        if i == 3:
            d.line([ox + 3, 12, ox + 6, 9], fill=(60, 50, 40, 255))
    ox = 4 * 16
    d.rectangle([ox, 0, ox + 15, 15], fill=(18, 16, 20, 255))
    for k in range(0, 16, 5):
        d.line([ox + k, 0, ox + k + 1, 15], fill=(36, 28, 24, 255), width=2)
    d.line([ox, 15, ox + 15, 15], fill=(8, 6, 10, 255))
    ox = 5 * 16
    d.rectangle([ox, 0, ox + 15, 15], fill=(26, 32, 26, 255))
    d.ellipse([ox + 1, 3, ox + 14, 15], fill=(56, 54, 60, 255), outline=OUTLINE)
    d.ellipse([ox + 4, 5, ox + 9, 9], fill=(84, 82, 90, 255))
    ox = 6 * 16
    d.rectangle([ox, 0, ox + 15, 15], fill=(30, 18, 20, 255))
    for k in range(4):
        x = ox + 2 + k * 4
        d.polygon([(x, 14), (x + 2, 4 + (k % 2) * 3), (x + 3, 14)], fill=(130, 30, 40, 255), outline=(60, 10, 16, 255))
    # colour-independent danger marker: a dotted border pattern
    for k in range(0, 16, 3):
        d.point([ox + k, 0], fill=(230, 210, 120, 255))
        d.point([ox + k, 15], fill=(230, 210, 120, 255))
    ox = 7 * 16
    d.rectangle([ox, 0, ox + 15, 15], fill=(26, 32, 26, 255))
    d.ellipse([ox + 2, 2, ox + 13, 13], outline=(110, 180, 230, 255))
    d.line([ox + 7, 4, ox + 7, 11], fill=(150, 210, 250, 255))
    d.line([ox + 4, 7, ox + 11, 7], fill=(150, 210, 250, 255))
    img.save(os.path.join(OUT, "tiles", "forest.png"))
    return {"tileSize": 16, "order": names}


def make_icons():
    names = ["warrior_whirlwind", "warrior_shield_charge", "warrior_battle_cry", "dash", "potion", "special"]
    img = Image.new("RGBA", (48 * len(names), 48), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for i, n in enumerate(names):
        ox = i * 48
        d.rectangle([ox, 0, ox + 47, 47], fill=(22, 18, 28, 255), outline=(90, 80, 110, 255))
        c = ox + 24
        if n == "warrior_whirlwind":
            for r in (8, 14, 20):
                d.arc([c - r, 24 - r, c + r, 24 + r], 20, 300, fill=STEEL[3], width=2)
        elif n == "warrior_shield_charge":
            d.polygon([(c - 10, 10), (c + 10, 10), (c + 10, 26), (c, 38), (c - 10, 26)], fill=CRIMSON[1], outline=STEEL[3])
            for k in range(3):
                d.line([ox + 4, 18 + k * 6, ox + 12, 18 + k * 6], fill=STEEL[2], width=2)
        elif n == "warrior_battle_cry":
            d.polygon([(c - 6, 18), (c + 2, 12), (c + 2, 36), (c - 6, 30)], fill=STEEL[2], outline=OUTLINE)
            for k in range(3):
                d.arc([c - 2 - k * 5, 24 - 8 - k * 4, c + 10 + k * 5, 24 + 8 + k * 4], 300, 60, fill=CRIMSON[2], width=2)
        elif n == "dash":
            for k in range(3):
                d.polygon([(ox + 8 + k * 10, 12), (ox + 18 + k * 10, 24), (ox + 8 + k * 10, 36)], outline=(150, 210, 250, 255))
        elif n == "potion":
            d.ellipse([c - 10, 18, c + 10, 40], fill=(170, 30, 40, 255), outline=OUTLINE)
            d.rectangle([c - 3, 8, c + 3, 18], fill=(150, 150, 170, 255), outline=OUTLINE)
        elif n == "special":
            d.line([ox + 10, 38, ox + 38, 10], fill=STEEL[3], width=3)
            d.arc([ox + 6, 6, ox + 42, 42], 200, 340, fill=(230, 210, 120, 255), width=2)
    img.save(os.path.join(OUT, "ui", "icons.png"))
    return {"size": 48, "order": names}


def main():
    for sub in ("characters", "enemies", "bosses", "tiles", "ui"):
        os.makedirs(os.path.join(OUT, sub), exist_ok=True)
    meta = {
        "note": "Generated test assets (tools/assetgen). Replace with final art keeping this metadata format.",
        "players": {"warrior": make_player_sheet()},
        "weapons": make_weapons(),
        "enemies": {
            "forest_ghoul": make_enemy("forest_ghoul", 32, [(58, 70, 54, 255), (88, 104, 80, 255)], (220, 230, 120, 255), "ghoul"),
            "forest_thorn_archer": make_enemy("forest_thorn_archer", 32, [(40, 52, 34, 255), (70, 84, 56, 255)], (230, 90, 70, 255), "archer"),
            "forest_rot_shaman": make_enemy("forest_rot_shaman", 32, [(52, 40, 58, 255), (84, 70, 90, 255)], (150, 230, 90, 255), "shaman"),
            "forest_sprout": make_enemy("forest_sprout", 24, [(48, 70, 36, 255), (92, 130, 60, 255)], (230, 90, 70, 255), "sprout"),
        },
        "bosses": {"forest_warden": make_boss()},
        "tiles": {"forest": make_tiles()},
        "icons": make_icons(),
    }
    with open(os.path.join(OUT, "sprites.json"), "w") as f:
        json.dump(meta, f, indent=2)
    print("assets written to", OUT)


if __name__ == "__main__":
    main()
