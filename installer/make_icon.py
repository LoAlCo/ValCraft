"""Builds installer/icon.ico: a Minecraft-style pixel sword in front of a Viking round shield
(Valheim). Original art, drawn here: wooden planks, iron rim with rivets and an iron boss, and a
diamond-blue sword on a 16x16 grid so it stays crisp at every size.

  python installer/make_icon.py   (needs ffmpeg on PATH to scale the sizes down)
"""
import math
import os
import struct
import subprocess
import tempfile
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "icon.ico")
N = 1024  # drawn at 4x 256, scaled down for smooth edges


def hash01(x, y, salt=0):
    h = (x * 73856093) ^ (y * 19349663) ^ (salt * 83492791)
    h ^= h >> 13
    h = (h * 0x5BD1E995) & 0xFFFFFFFF
    h ^= h >> 15
    return (h & 0xFFFF) / 65535.0


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def shade(c, f):
    return tuple(max(0, min(255, int(v * f))) for v in c)


# ---- the shield -----------------------------------------------------------------------------

PLANKS = [(138, 92, 52), (122, 80, 44), (148, 101, 58), (112, 72, 38), (132, 86, 48)]
IRON, IRON_LIGHT, IRON_DARK = (92, 98, 106), (150, 156, 164), (48, 52, 58)


def shield_pixel(x, y):
    cx, cy = N * 0.5, N * 0.48
    r = N * 0.44
    dx, dy = x - cx, y - cy
    d = math.hypot(dx, dy)
    if d > r:
        return None
    light = 1.0 + 0.18 * (-(dx + dy) / r)  # lit from the top left
    rim_in = r * 0.9
    boss = r * 0.24
    if d > rim_in:  # iron rim
        t = (d - rim_in) / (r - rim_in)
        c = mix(IRON_LIGHT, IRON_DARK, abs(t - 0.4))
        # rivets around the rim
        a = math.atan2(dy, dx)
        k = round(a / (math.pi / 6)) * (math.pi / 6)
        rx, ry = cx + math.cos(k) * (rim_in + r) / 2, cy + math.sin(k) * (rim_in + r) / 2
        if math.hypot(x - rx, y - ry) < r * 0.035:
            c = IRON_LIGHT if (x - rx) + (y - ry) < 0 else IRON_DARK
        return shade(c, light)
    if d < boss:  # iron boss, a dome
        t = d / boss
        hl = math.hypot(dx + boss * 0.35, dy + boss * 0.35) / boss
        c = mix(IRON_LIGHT, IRON_DARK, min(1.0, 0.25 + hl * 0.7))
        if t > 0.86:
            c = IRON_DARK
        return shade(c, light)
    # wooden planks, vertical, with grain
    plank = int((x - (cx - r)) / (N * 0.085))
    base = PLANKS[plank % len(PLANKS)]
    seam = ((x - (cx - r)) % (N * 0.085)) < N * 0.006
    grain = 0.9 + 0.12 * math.sin(y * 0.045 + hash01(plank, 0) * 6.0 + math.sin(y * 0.011 + plank) * 2.0)
    c = shade(base, grain * (0.55 if seam else 1.0))
    # darker toward the rim, and a painted ring inside it (red, Viking-style)
    edge = (d - boss) / (rim_in - boss)
    if 0.78 < edge < 0.9:
        c = mix(c, (150, 34, 30), 0.75)
    c = shade(c, 1.0 - 0.25 * max(0.0, edge - 0.6))
    return shade(c, light)


# ---- the sword (16x16 pixel art, blade up and to the right) ---------------------------------

def sword_sprite():
    px = {}
    for t in range(9):  # blade
        cx, cy = 5 + t, 10 - t
        px[(cx, cy)] = "L"
        for c in ((cx + 1, cy), (cx, cy - 1)):
            px.setdefault(c, "C" if t < 8 else "L")
        if t < 7:
            px.setdefault((cx + 1, cy + 1), "D")
    for k in range(-2, 3):  # guard, across the blade
        px[(4 + k, 11 + k)] = "G"
    px[(4, 11)] = "g"
    px[(3, 12)] = "B"  # grip
    px[(2, 13)] = "b"
    px[(1, 14)] = "P"  # pommel
    outline = {}
    for (x, y) in px:
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if (nx, ny) not in px and 0 <= nx < 16 and 0 <= ny < 16:
                outline[(nx, ny)] = "O"
    px.update({k: v for k, v in outline.items() if k not in px})
    return px


SWORD_COLORS = {
    "L": (186, 255, 244), "C": (52, 226, 204), "D": (24, 160, 150), "O": (12, 40, 44),
    "G": (62, 128, 132), "g": (104, 176, 176), "B": (150, 102, 54), "b": (112, 74, 38), "P": (62, 128, 132),
}


def render():
    rgba = bytearray(N * N * 4)
    for y in range(N):
        for x in range(N):
            c = shield_pixel(x, y)
            if c:
                i = (y * N + x) * 4
                rgba[i:i + 4] = bytes(c) + b"\xff"
    sprite = sword_sprite()
    cell = N // 18  # the sword covers most of the icon, across the shield
    ox, oy = int(N * 0.06), int(N * 0.08)
    for (sx, sy), k in sprite.items():
        col = bytes(SWORD_COLORS[k]) + b"\xff"
        for y in range(oy + sy * cell, oy + (sy + 1) * cell):
            for x in range(ox + sx * cell, ox + (sx + 1) * cell):
                if 0 <= x < N and 0 <= y < N:
                    i = (y * N + x) * 4
                    rgba[i:i + 4] = col
    return rgba


def png(width, height, rgba):
    raw = b"".join(b"\x00" + bytes(rgba[y * width * 4:(y + 1) * width * 4]) for y in range(height))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def main():
    with tempfile.TemporaryDirectory() as tmp:
        big = os.path.join(tmp, "big.png")
        open(big, "wb").write(png(N, N, render()))
        open(os.path.join(HERE, "icon-preview.png"), "wb").write(open(big, "rb").read())
        images = []
        for s in (16, 24, 32, 48, 64, 128, 256):
            out = os.path.join(tmp, f"{s}.png")
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", big, "-vf", f"scale={s}:{s}:flags=area", out], check=True)
            images.append((s, open(out, "rb").read()))
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries, data = b"", b""
    for size, blob in images:
        entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(blob), offset + len(data))
        data += blob
    open(OUT, "wb").write(header + entries + data)
    print("wrote", OUT, os.path.getsize(OUT), "bytes")


if __name__ == "__main__":
    main()
