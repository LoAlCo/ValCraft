"""Makes 16x16 Minecraft-style item textures from Valheim's own icons, the base the user asked for.

The icons come from the game (ValCraft's [Debug] ExportIcons saves them to
<profile>/BepInEx/ValCraft icons/<prefab>.png); they're Valheim's art, so they stay out of the repo
and only the pixel versions made here go into the mod. Per icon: crop to the item, shrink to 14x14
by area (weighted by alpha, so whatever sits behind transparent pixels doesn't bleed in), cut the
edge at half alpha, reduce to a few colours, and add a dark outline like Minecraft's items.

  python tools/pixelize_icons.py "<profile>/BepInEx/ValCraft icons"   (needs ffmpeg on PATH)
"""
import os
import struct
import subprocess
import sys
import zlib

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fabric", "src", "main", "resources",
                   "assets", "valcraft", "textures", "item")

# Valheim prefab -> (ValCraft item, colours to keep[, alpha cut]). A higher cut leaves out a soft glow
# around the item. The deer trophy, fuling totem and wishbone are drawn by hand in make_item_textures.py.
ICONS = {
    "AncientSeed": ("ancient_seed", 6),
    "Bell": ("bell", 9),
    "BellFragment": ("bell_fragment", 9),
    "CryptKey": ("swamp_key", 5),
    "DragonEgg": ("dragon_egg", 6),
    "DragonTear": ("dragon_tear", 5, 0.7),
    "FaderDrop": ("kindled_ribs", 6),
    "HardAntler": ("hard_antler", 5),
    "QueenDrop": ("majestic_carapace", 6),
    "DvergrKey": ("sealbreaker", 6),
    "DvergrKeyFragment": ("sealbreaker_fragment", 6),
    "TrophyBonemass": ("bonemass_trophy", 7),
    "TrophyDragonQueen": ("moder_trophy", 7),
    "TrophyEikthyr": ("eikthyr_trophy", 7),
    "TrophyFader": ("fader_trophy", 7),
    "TrophyGoblinKing": ("yagluth_trophy", 9),
    "TrophySeekerQueen": ("queen_trophy", 7),
    "TrophyTheElder": ("elder_trophy", 7),
    "WitheredBone": ("withered_bone", 5),
    "YagluthDrop": ("torn_spirit", 6),
}

INNER = 14  # the item inside a 1-pixel outline


def load(path):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-f", "rawvideo", "-pix_fmt", "rgba", "-"],
                         check=True, capture_output=True).stdout
    probe = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height",
                            "-of", "csv=p=0", path], check=True, capture_output=True, text=True).stdout.strip()
    w, h = (int(v) for v in probe.split(","))
    return w, h, [tuple(raw[i:i + 4]) for i in range(0, len(raw), 4)]


def crop_box(w, h, px, cut=0.5):
    limit = max(60, int(cut * 230))
    xs = [i % w for i, p in enumerate(px) if p[3] > limit]
    ys = [i // w for i, p in enumerate(px) if p[3] > limit]
    x0, x1, y0, y1 = min(xs), max(xs) + 1, min(ys), max(ys) + 1
    size = max(x1 - x0, y1 - y0)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    return cx - size / 2, cy - size / 2, size


def shrink(w, h, px, box):
    """Area-average the square box down to INNER x INNER, colours weighted by alpha."""
    bx, by, size = box
    step = size / INNER
    out = []
    for ty in range(INNER):
        for tx in range(INNER):
            sx0, sy0 = bx + tx * step, by + ty * step
            sx1, sy1 = sx0 + step, sy0 + step
            r = g = b = a = area = 0.0
            for y in range(int(sy0), int(sy1 + 0.999)):
                oy = min(sy1, y + 1) - max(sy0, y)
                if oy <= 0:
                    continue
                for x in range(int(sx0), int(sx1 + 0.999)):
                    ox = min(sx1, x + 1) - max(sx0, x)
                    if ox <= 0:
                        continue
                    wgt = ox * oy
                    area += wgt
                    if 0 <= x < w and 0 <= y < h:
                        pr, pg, pb, pa = px[y * w + x]
                        al = pa / 255.0 * wgt
                        r += pr * al
                        g += pg * al
                        b += pb * al
                        a += al
            out.append((r / a, g / a, b / a, a / area) if a > 0 else (0, 0, 0, 0))
    return out


def quantize(colours, n):
    """Median cut down to n colours; returns the palette."""
    boxes = [list(colours)]
    while len(boxes) < n:
        boxes.sort(key=lambda b: max(max(c[i] for c in b) - min(c[i] for c in b) for i in range(3)) if len(b) > 1 else -1)
        box = boxes.pop()
        if len(box) < 2:
            boxes.append(box)
            break
        ch = max(range(3), key=lambda i: max(c[i] for c in box) - min(c[i] for c in box))
        box.sort(key=lambda c: c[ch])
        mid = len(box) // 2
        boxes += [box[:mid], box[mid:]]
    return [tuple(sum(c[i] for c in b) / len(b) for i in range(3)) for b in boxes if b]


def nearest(c, palette):
    return min(palette, key=lambda p: sum((c[i] - p[i]) ** 2 for i in range(3)))


def pixelize(path, colours, cut=0.5):
    w, h, px = load(path)
    small = shrink(w, h, px, crop_box(w, h, px, cut))
    solid = {(i % INNER + 1, i // INNER + 1): c[:3] for i, c in enumerate(small) if c[3] >= cut}
    palette = quantize(list(solid.values()), colours)
    # a touch more contrast and saturation than the soft render, like Minecraft's flat shading
    def punch(c):
        grey = sum(c) / 3
        return tuple(max(0, min(255, int((grey + (v - grey) * 1.15 - 128) * 1.08 + 128))) for v in c)
    out = {pos: punch(nearest(c, palette)) for pos, c in solid.items()}
    darkest = min(palette, key=sum)
    edge = tuple(int(v * 0.35) for v in darkest)
    for (x, y) in list(solid):
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if (nx, ny) not in solid and 0 <= nx < 16 and 0 <= ny < 16:
                out[(nx, ny)] = edge
    return out


def png(path, px):
    rows = b"".join(b"\x00" + b"".join(bytes(px[(x, y)]) + b"\xff" if (x, y) in px else b"\x00\x00\x00\x00" for x in range(16))
                    for y in range(16))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 16, 16, 8, 6, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


def main():
    src = sys.argv[1]
    os.makedirs(OUT, exist_ok=True)
    for prefab, spec in ICONS.items():
        name, colours = spec[0], spec[1]
        cut = spec[2] if len(spec) > 2 else 0.5
        path = os.path.join(src, prefab + ".png")
        if not os.path.exists(path):
            print("missing", path)
            continue
        png(os.path.join(OUT, name + ".png"), pixelize(path, colours, cut))
        print("wrote", name)


if __name__ == "__main__":
    main()
