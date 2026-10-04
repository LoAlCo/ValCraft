"""Draws previews of ValCraft's 3D weapon models (the resource pack's item models) without the game,
textured: each model standing up from the front, and turned three-quarters. Needs Pillow.

  python tools/preview_models.py out.png id [id ...]
"""
import json
import math
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "fabric", "src", "main", "resources", "resourcepacks", "valcraft_3d_weapons", "assets", "valcraft")
CELL = 360
SHADE = {"up": 1.0, "south": 0.92, "north": 0.8, "east": 0.85, "west": 0.72, "down": 0.6}


def corners(el):
    (x0, y0, z0), (x1, y1, z1) = el["from"], el["to"]
    # top-left, top-right, bottom-left of each face as Minecraft maps its texture
    return {
        "south": ((x0, y1, z1), (x1, y1, z1), (x0, y0, z1)),
        "north": ((x1, y1, z0), (x0, y1, z0), (x1, y0, z0)),
        "east": ((x1, y1, z1), (x1, y1, z0), (x1, y0, z1)),
        "west": ((x0, y1, z0), (x0, y1, z1), (x0, y0, z0)),
        "up": ((x0, y1, z0), (x1, y1, z0), (x0, y1, z1)),
        "down": ((x0, y0, z1), (x1, y0, z1), (x0, y0, z0)),
    }


def view(p, yaw):
    a = math.radians(yaw)
    x, y, z = p[0] - 8, p[1] - 8, p[2] - 8
    return (x * math.cos(a) + z * math.sin(a), y, -x * math.sin(a) + z * math.cos(a))


def render(model, tex, yaw):
    img = Image.new("RGB", (CELL, CELL), (60, 64, 64))
    d = ImageDraw.Draw(img)
    size = tex.size[0]
    k = size / 16
    quads = []
    span = max(max(abs(c - 8) for c in el["from"] + el["to"]) for el in model["elements"]) + 1
    sc = CELL / (2 * span)
    for el in model["elements"]:
        for face, (tl, tr, bl) in corners(el).items():
            uv = el["faces"][face]["uv"]
            u0, v0, u1, v1 = (int(round(c * k)) for c in uv)
            fw, fh = max(1, u1 - u0), max(1, v1 - v0)
            P = [view(p, yaw) for p in (tl, tr, bl)]
            ex = [(P[1][i] - P[0][i]) for i in range(3)]
            ey = [(P[2][i] - P[0][i]) for i in range(3)]
            nz = ex[0] * ey[1] - ex[1] * ey[0]
            if nz >= 0:  # facing away (the screen's y goes up here)
                continue
            for j in range(fh):
                for i in range(fw):
                    c = tex.getpixel((min(size - 1, u0 + i), min(size - 1, v0 + j)))
                    if c[3] == 0:
                        continue
                    pts = []
                    for a, b in ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)):
                        q = [P[0][n] + ex[n] * a / fw + ey[n] * b / fh for n in range(3)]
                        pts.append(q)
                    depth = sum(q[2] for q in pts) / 4
                    col = tuple(int(ch * SHADE[face]) for ch in c[:3])
                    quads.append((depth, [(CELL / 2 + q[0] * sc, CELL / 2 - q[1] * sc) for q in pts], col))
    for _, pts, col in sorted(quads, key=lambda q: q[0]):
        d.polygon(pts, fill=col)
    return img


def main():
    out, ids = sys.argv[1], sys.argv[2:]
    img = Image.new("RGB", (CELL * len(ids), CELL * 2), (60, 64, 64))
    for n, item_id in enumerate(ids):
        model = json.load(open(os.path.join(ASSETS, "models", "item", "3d", item_id + ".json"), encoding="utf-8"))
        for el in model["elements"]:
            el.pop("rotation", None)  # previewed standing up
        tex = Image.open(os.path.join(ASSETS, "textures", "item", "3d", item_id + ".png")).convert("RGBA")
        img.paste(render(model, tex, 0), (n * CELL, 0))
        img.paste(render(model, tex, 40), (n * CELL, CELL))
        ImageDraw.Draw(img).text((n * CELL + 4, 4), item_id, fill=(255, 255, 255))
    img.save(out)


if __name__ == "__main__":
    main()
