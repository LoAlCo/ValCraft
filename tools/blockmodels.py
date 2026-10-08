"""Builds blocky Minecraft item models the way they're made by hand in Blockbench: a weapon is put
together from boxes (blade, guard, gem, grip wrap, pommel...), each face painted as pixel art in the
style of its material (metal with a bright edge, wood grain, a diagonal leather wrap, a cut gem, gold
trim), at one texel per unit. The model is built standing up (tip up, along y, centred on x = z = 8)
and turned 45 degrees onto the diagonal for holding, like Minecraft's own swords.

Used by tools/weapons_3d.py. Writes the model JSON and its texture into the ValCraft 3D Weapons
resource pack.
"""
import json
import math
import os
import struct
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
PACK = os.path.join(HERE, "..", "fabric", "src", "main", "resources", "resourcepacks", "valcraft_3d_weapons")
ASSETS = os.path.join(PACK, "assets", "valcraft")

FACES = ("north", "east", "south", "west", "up", "down")


def hexc(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def noise(x, y, seed):
    n = (x * 374761393 + y * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


# First person: no weapon looks thicker than this front to back (units after scaling: a sledge's head).
FP_THICK = 3.0

class Mat:
    """A material: three tones (light, mid, dark) and a style that paints a face with them."""

    def __init__(self, style, light, mid, dark, accent=None, glow=0):
        """glow: 0-15, the part lights itself up (gems, runes)."""
        self.style = style
        self.glow = glow
        self.light, self.mid, self.dark = hexc(light), hexc(mid), hexc(dark)
        self.accent = hexc(accent) if accent else self.light

    def paint(self, w, h, face, seed):
        px = [[self.mid] * w for _ in range(h)]
        getattr(self, "_" + self.style)(px, w, h, face, seed)
        if self.style not in ("gem", "blade") and w >= 3 and h >= 3:
            # a bevel round every face: lit along the top and left, shaded along the bottom and right
            for x in range(w):
                px[0][x] = mix(px[0][x], self.light, 0.45)
                px[h - 1][x] = mix(px[h - 1][x], self.dark, 0.55)
            for y in range(h):
                px[y][0] = mix(px[y][0], self.light, 0.35)
                px[y][w - 1] = mix(px[y][w - 1], self.dark, 0.5)
        return px

    # -- styles -----------------------------------------------------------------------------
    def _metal(self, px, w, h, face, seed):
        for y in range(h):
            for x in range(w):
                # lit at the left edge, mostly its middle tone, shading off to the right
                t = x / max(1, w - 1)
                c = self.light if x == 0 else (self.mid if t < 0.65 else mix(self.mid, self.dark, (t - 0.65) / 0.35))
                if noise(x, y, seed) > 0.93:
                    c = mix(c, self.light, 0.6)
                px[y][x] = c
        if face == "up":
            for x in range(w):
                px[0][x] = self.light

    def _blade(self, px, w, h, face, seed):
        """A blade's broad face: bright cutting edges, a darker fuller down the middle."""
        self._metal(px, w, h, face, seed)
        if face in ("north", "south") and w >= 3:
            for y in range(h):
                px[y][0] = self.accent
                px[y][w - 1] = mix(self.light, self.mid, 0.3)
                if w >= 5:
                    px[y][w // 2] = mix(self.mid, self.dark, 0.6)

    def _wood(self, px, w, h, face, seed):
        for y in range(h):
            for x in range(w):
                n = noise(x, y // 3, seed)
                c = self.mid
                if n > 0.7:
                    c = self.dark
                elif n < 0.15:
                    c = self.light
                px[y][x] = c

    def _wrap(self, px, w, h, face, seed):
        """Leather or cloth wound round a grip: diagonal bands."""
        for y in range(h):
            for x in range(w):
                band = (x + y) % 3
                px[y][x] = self.dark if band == 0 else (self.light if band == 1 and y % 3 == 0 else self.mid)

    def _gold(self, px, w, h, face, seed):
        for y in range(h):
            for x in range(w):
                edge = x == 0 or y == 0 or x == w - 1 or y == h - 1
                px[y][x] = self.dark if edge and (x == w - 1 or y == h - 1) else (self.light if edge else self.mid)
        if w >= 3 and h >= 3:
            px[1][1] = self.accent

    def _gem(self, px, w, h, face, seed):
        cx, cy = (w - 1) / 2, (h - 1) / 2
        for y in range(h):
            for x in range(w):
                d = max(abs(x - cx) / max(1, cx), abs(y - cy) / max(1, cy))
                px[y][x] = self.dark if d > 0.8 else (self.mid if d > 0.35 else self.light)
        if w >= 3 and h >= 3:
            px[int(cy) - (1 if h > 3 else 0)][int(cx) - (1 if w > 3 else 0)] = (255, 255, 255)

    def _plain(self, px, w, h, face, seed):
        for y in range(h):
            for x in range(w):
                edge = x == w - 1 or y == h - 1
                px[y][x] = self.dark if edge else (self.light if (x == 0 or y == 0) else self.mid)

    def _rough(self, px, w, h, face, seed):
        """Stone, bone, fur: a speckled surface."""
        for y in range(h):
            for x in range(w):
                n = noise(x, y, seed)
                px[y][x] = self.dark if n > 0.75 else (self.light if n < 0.2 else self.mid)


class Model:
    def __init__(self, item_id, length=32, metres=None, grip=None):
        """length: how long the weapon is built, in units (texels); metres: how long the Valheim one
        is, so it's held at its size next to the Viking (a Minecraft player is 1.8 blocks, a Viking
        1.8 m); grip: where along it the hand holds it."""
        self.item_id = item_id
        self.length = length
        self.metres = metres
        self.grip = grip if grip is not None else length * 0.15
        self.boxes = []

    def box(self, x0, y0, z0, x1, y1, z1, mat):
        """A box in model units; x and z are relative to the centre line (0), y from the grip end (0)."""
        self.boxes.append(((x0, y0, z0), (x1, y1, z1), mat))
        return self

    def cbox(self, w, y0, y1, d, mat, x=0.0, z=0.0):
        """A box centred on the weapon's line: w wide (x), d deep (z), from y0 to y1."""
        return self.box(x - w / 2, y0, z - d / 2, x + w / 2, y1, z + d / 2, mat)

    def separate(self):
        """No z-fighting: a box with a face lying in the same plane as an earlier box's face, where
        the two overlap, is pushed out a little there (the later box is the detail on top)."""
        eps = 0.12
        boxes = [[list(lo), list(hi), m] for lo, hi, m in self.boxes]
        for j in range(len(boxes)):
            for i in range(j):
                (a0, a1, _), (b0, b1, _) = boxes[i], boxes[j]
                for ax in range(3):
                    others = [o for o in range(3) if o != ax]
                    if not all(min(a1[o], b1[o]) - max(a0[o], b0[o]) > 1e-6 for o in others):
                        continue  # their faces across this axis don't overlap
                    if abs(b0[ax] - a0[ax]) < 1e-6:
                        b0[ax] -= eps
                    if abs(b1[ax] - a1[ax]) < 1e-6:
                        b1[ax] += eps
        self.boxes = [(tuple(lo), tuple(hi), m) for lo, hi, m in boxes]

    def build(self, scale=None):
        self.separate()
        size, placed, tex = pack(self.boxes)
        # elements: stood up along y, centred on x = z = 8, the whole weapon centred on y = 8
        k = 16 / size
        # its size in the hand: its real length (Minecraft holds a sword sprite at 0.85 in third person)
        s = scale or (min(1.7, self.metres * 16 / (self.length * 0.85)) if self.metres else 19.0 / self.length)
        # the grip goes where a Minecraft sword's handle is: 6.4 units down the diagonal from the middle
        off = 8 - 6.4 / s - self.grip
        off = max(-16.0, min(32.0 - self.length, off))  # Minecraft only allows parts within -16..32
        elements = []
        for i, ((x0, y0, z0), (x1, y1, z1), mat) in enumerate(self.boxes):
            el = {"from": [round(8 + x0, 3), round(off + y0, 3), round(8 + z0, 3)],
                  "to": [round(8 + x1, 3), round(off + y1, 3), round(8 + z1, 3)],
                  "rotation": {"angle": -45, "axis": "z", "origin": [8, 8, 8]},
                  "faces": {}}
            for f in FACES:
                x, y, fw, fh = placed[(i, f)]
                el["faces"][f] = {"uv": [x * k, y * k, (x + fw) * k, (y + fh) * k], "texture": "#0"}
            if mat.glow:
                el["light_emission"] = mat.glow
            elements.append(el)
        # in first person no bigger than a little over Minecraft's own, or a greatsword fills the view;
        # block-like heads (sledges) smaller still, by how thick they are front to back (blades,
        # axes and picks are flat that way, so they keep their size)
        thick = max(max(abs(z0), abs(z1)) for (_, _, z0), (_, _, z1), _ in self.boxes) * 2
        fp = 0.68 * min(s, 0.75)
        fp = min(fp, FP_THICK / max(thick, 1e-6))
        display = {
            "thirdperson_righthand": {"rotation": [0, -90, 55], "translation": [0, 4.0, 0.5], "scale": [0.85 * s] * 3},
            "thirdperson_lefthand": {"rotation": [0, 90, -55], "translation": [0, 4.0, 0.5], "scale": [0.85 * s] * 3},
            "firstperson_righthand": {"rotation": [0, -90, 25], "translation": [1.13, 3.2, 1.13], "scale": [fp] * 3},
            "firstperson_lefthand": {"rotation": [0, 90, -25], "translation": [1.13, 3.2, 1.13], "scale": [fp] * 3},
            "ground": {"rotation": [0, 0, 0], "translation": [0, 2, 0], "scale": [0.5 * s] * 3},
            "head": {"rotation": [0, 180, 0], "translation": [0, 13, 7], "scale": [s] * 3},
            "fixed": {"rotation": [0, 180, 0], "translation": [0, 0, 0], "scale": [s] * 3},
        }
        lo = min(min(e["from"]) for e in elements)
        hi = max(max(e["to"]) for e in elements)
        if lo < -16 or hi > 32:
            raise SystemExit(f"{self.item_id}: parts reach {lo:.1f}..{hi:.1f}, outside the -16..32 Minecraft allows")
        model = {"texture_size": [size, size],
                 "textures": {"0": f"valcraft:item/3d/{self.item_id}", "particle": f"valcraft:item/{self.item_id}"},
                 "elements": elements, "display": display}
        path = os.path.join(ASSETS, "models", "item", "3d", self.item_id + ".json")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        json.dump(model, open(path, "w", encoding="utf-8"), indent=1)
        write_png(os.path.join(ASSETS, "textures", "item", "3d", self.item_id + ".png"), tex)
        item = {"model": {"type": "minecraft:select", "property": "minecraft:display_context",
                          "cases": [{"when": ["gui", "fixed"], "model": {"type": "minecraft:model", "model": f"valcraft:item/{self.item_id}"}}],
                          "fallback": {"type": "minecraft:model", "model": f"valcraft:item/3d/{self.item_id}"}}}
        path = os.path.join(ASSETS, "items", self.item_id + ".json")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        json.dump(item, open(path, "w", encoding="utf-8"), indent=2)
        return len(elements), size


def pack(boxes):
    """Paints every face of the boxes and packs them into one square texture: (size, {(box, face): (x, y, w, h)}, pixels)."""
    # one face per box side, sized in texels
    faces = []
    for i, ((x0, y0, z0), (x1, y1, z1), mat) in enumerate(boxes):
        w, h, d = x1 - x0, y1 - y0, z1 - z0
        dims = {"north": (w, h), "south": (w, h), "east": (d, h), "west": (d, h), "up": (w, d), "down": (w, d)}
        for f in FACES:
            fw, fh = dims[f]
            faces.append((i, f, max(1, math.ceil(fw - 1e-6)), max(1, math.ceil(fh - 1e-6))))
    # shelf-pack the faces into a square texture
    size = 16
    while True:
        placed, x, y, row = {}, 0, 0, 0
        ok = True
        for i, f, fw, fh in sorted(faces, key=lambda t: -t[3]):
            if x + fw > size:
                x, y, row = 0, y + row, 0
            if y + fh > size or fw > size:
                ok = False
                break
            placed[(i, f)] = (x, y, fw, fh)
            x += fw
            row = max(row, fh)
        if ok:
            break
        size *= 2
    tex = [[(0, 0, 0, 0)] * size for _ in range(size)]
    for (i, f), (x, y, fw, fh) in placed.items():
        mat = boxes[i][2]
        px = mat.paint(fw, fh, f, i * 7 + FACES.index(f))
        for yy in range(fh):
            for xx in range(fw):
                tex[y + yy][x + xx] = px[yy][xx] + (255,)
    return size, placed, tex


def write_png(path, tex):
    h, w = len(tex), len(tex[0])
    rows = b"".join(b"\x00" + b"".join(bytes(p) for p in row) for row in tex)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


def write_pack_meta():
    meta = {"pack": {"description": "ValCraft: Valheim's weapons as 3D models", "pack_format": 97, "min_format": 97, "max_format": 999}}
    os.makedirs(PACK, exist_ok=True)
    json.dump(meta, open(os.path.join(PACK, "pack.mcmeta"), "w", encoding="utf-8"), indent=2)
