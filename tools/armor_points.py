"""Reads the armor samples ValCraft's [Debug] ExportArmor writes (each Valheim armor piece as worn:
coloured surface points and the skeleton) and draws them front, side and back, for checking and
for drawing ValCraft's Minecraft versions from.

  python tools/armor_points.py out.png "<profile>/BepInEx/ValCraft armor" Prefab [Prefab ...]
"""
import os
import sys

from PIL import Image, ImageDraw


def load(path):
    bones, pts = {}, []
    for line in open(path, encoding="utf-8"):
        p = line.split()
        if not p or p[0].startswith("#"):
            continue
        if p[0] == "bone":
            bones[p[1]] = tuple(float(v) for v in p[2:5])
        elif p[0] == "p":
            pts.append((tuple(float(v) for v in p[1:4]), tuple(float(v) for v in p[4:7]), tuple(int(v) for v in p[7:10])))
    return bones, pts


def view(pts, axis_x, axis_y, depth, size=240, scale=110.0, cx=0.0, cy=1.0):
    """An orthographic picture: the nearest point (by depth(p)) per pixel."""
    img = Image.new("RGB", (size, size), (52, 56, 62))
    zb = {}
    for p, n, c in pts:
        x = int(size / 2 + (axis_x(p) - cx) * scale)
        y = int(size / 2 - (axis_y(p) - cy) * scale)
        d = depth(p)
        for dx in (0, 1):
            for dy in (0, 1):
                k = (x + dx, y + dy)
                if 0 <= k[0] < size and 0 <= k[1] < size and d > zb.get(k, -1e9):
                    zb[k] = d
                    img.putpixel(k, c)
    return img


def main():
    out, folder, names = sys.argv[1], sys.argv[2], sys.argv[3:]
    cell = 240
    sheet = Image.new("RGB", (cell * 3, cell * len(names)), (40, 40, 40))
    for i, name in enumerate(names):
        bones, pts = load(os.path.join(folder, name + ".txt"))
        ys = [p[1] for p, _, _ in pts]
        cy = (min(ys) + max(ys)) / 2
        span = max(max(ys) - min(ys), max(abs(p[0]) for p, _, _ in pts) * 2, 0.4)
        scale = cell * 0.9 / span
        sheet.paste(view(pts, lambda p: -p[0], lambda p: p[1], lambda p: p[2], cell, scale, 0, cy), (0, i * cell))  # front (facing +z)
        sheet.paste(view(pts, lambda p: p[2], lambda p: p[1], lambda p: -p[0], cell, scale, 0, cy), (cell, i * cell))  # right side
        sheet.paste(view(pts, lambda p: p[0], lambda p: p[1], lambda p: -p[2], cell, scale, 0, cy), (2 * cell, i * cell))  # back
        ImageDraw.Draw(sheet).text((4, i * cell + 4), f"{name} ({len(pts)})", fill=(255, 255, 255))
    sheet.save(out)


if __name__ == "__main__":
    main()
