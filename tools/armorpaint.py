"""Paints Minecraft armor textures (the equipment layers a player wears) as pixel art, at twice
Minecraft's resolution (128x64 for the 64x32 armor layout), the way armor is drawn by hand: each
side of each box of the armor model (head, body, arms, legs) is filled with a material (riveted
plate, chainmail, scale, leather, fur, quilting, cloth, bone, root, chitin, gold, gems) and then
detailed (belts, straps, trims, rivets, a nasal guard, eye slits...).

Coordinates are in the armor model's own texels (Minecraft's 64x32 layout), with fractions down to
half a texel: every texel is 2x2 pixels here. A face is addressed as (box, side) and drawn on with
its top-left as (0, 0); "front" is what faces forward when worn.

Used by tools/armor_sets.py.
"""
import math
import os

from PIL import Image

SCALE = 2  # pixels per model texel

# Minecraft's humanoid armor layout: texture offset and size (w, h, d) of each box.
BOXES = {
    "head": (0, 0, 8, 8, 8),
    "hat": (32, 0, 8, 8, 8),     # drawn a little bigger than the head, over it (visors, hood rims)
    "body": (16, 16, 8, 12, 4),
    "arm": (40, 16, 4, 12, 4),   # both arms (Minecraft mirrors it for the left)
    "leg": (0, 16, 4, 12, 4),    # both legs (mirrored for the left); boots in the main layer, trousers in the leggings layer
}


def hexc(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


def mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3)) + (max(a[3], b[3]) if len(a) > 3 else 255,)


def noise(x, y, seed):
    n = (x * 374761393 + y * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def face_rect(box, side):
    """(u, v, w, h) in model texels of one side of a box."""
    u, v, w, h, d = BOXES[box]
    return {
        "top": (u + d, v, w, d),
        "bottom": (u + d + w, v, w, d),
        "right": (u, v + d, d, h),       # the wearer's right side
        "front": (u + d, v + d, w, h),
        "left": (u + d + w, v + d, d, h),
        "back": (u + d + w + d, v + d, w, h),
    }[side]


SIDES = ("top", "bottom", "right", "front", "left", "back")
AROUND = ("right", "front", "left", "back")


class Mat:
    """A material: tones (light, mid, dark, accent) and a style that paints it."""

    def __init__(self, style, light, mid, dark, accent=None, scale=1.0):
        self.style = style
        self.light, self.mid, self.dark = hexc(light), hexc(mid), hexc(dark)
        self.accent = hexc(accent) if accent else self.light
        self.scale = scale

    def at(self, x, y, w, h, seed):
        """The colour of pixel (x, y) of a w x h pixel area."""
        return getattr(self, "_" + self.style)(x, y, w, h, seed)

    def _plain(self, x, y, w, h, seed):
        return self.mid

    def _cloth(self, x, y, w, h, seed):
        # a fine weave and a little mottling
        c = self.mid if (x + y) % 2 == 0 else mix(self.mid, self.dark, 0.18)
        n = noise(x // 2, y // 3, seed)
        return mix(c, self.dark, 0.35) if n > 0.85 else (mix(c, self.light, 0.3) if n < 0.08 else c)

    def _leather(self, x, y, w, h, seed):
        n = noise(x // 2, y // 2, seed)
        c = mix(self.mid, self.dark, 0.4) if n > 0.78 else (mix(self.mid, self.light, 0.35) if n < 0.12 else self.mid)
        return mix(c, self.dark, 0.15 * (y / max(1, h - 1)))  # a touch darker lower down

    def _fur(self, x, y, w, h, seed):
        # strands hanging down: streaks that change every few pixels down
        strand = noise(x, y // 3 + (x % 3), seed)
        c = self.dark if strand > 0.72 else (self.light if strand < 0.22 else self.mid)
        if noise(x, y, seed + 5) > 0.93:
            c = self.accent
        return c

    def _chain(self, x, y, w, h, seed):
        # rings: a bright top, dark gaps, staggered rows of 2x2 links
        row = y // 2
        col = (x + (row % 2)) // 2
        lx, ly = (x + (row % 2)) % 2, y % 2
        if ly == 0 and lx == 0:
            return self.light
        if ly == 1 and lx == 1:
            return self.dark
        return self.mid if noise(col, row, seed) > 0.15 else mix(self.mid, self.dark, 0.5)

    def _scale(self, x, y, w, h, seed):
        # overlapping scales, rows of 4-pixel-wide rounded plates, each lit on top
        size = max(3, int(4 * self.scale))
        row = y // (size - 1)
        sx = (x + (row % 2) * (size // 2)) % size
        sy = y % (size - 1)
        if sy == 0:
            return self.light if 0 < sx < size - 1 else self.mid
        if sx == size - 1 or sy == size - 2:
            return self.dark
        return self.mid if noise(x, y, seed) > 0.1 else self.light

    def _plate(self, x, y, w, h, seed):
        # brushed metal: lit at the top, shaded towards the bottom, faint streaks
        t = y / max(1, h - 1)
        c = mix(self.light, self.mid, min(1.0, t * 2.2)) if t < 0.45 else mix(self.mid, self.dark, (t - 0.45) / 0.55 * 0.7)
        if noise(x, y // 4, seed) > 0.9:
            c = mix(c, self.light, 0.35)
        return c

    def _quilt(self, x, y, w, h, seed):
        # padded cloth: diamond stitching
        s = max(4, int(6 * self.scale))
        a, b = (x + y) % s, (x - y) % s
        if a == 0 or b == 0:
            return self.dark
        return self.light if a == 1 or b == 1 else self.mid

    def _bone(self, x, y, w, h, seed):
        n = noise(x // 2, y, seed)
        c = self.light if n < 0.3 else (self.mid if n < 0.8 else self.dark)
        return c

    def _root(self, x, y, w, h, seed):
        # twisted roots running up and down, moss in between
        wave = int(2 * math.sin(y / 3.0 + x * 0.7 + seed))
        lane = (x + wave) % 5
        if lane == 0:
            return self.dark
        if lane in (1, 2):
            return self.light if noise(x, y, seed) > 0.5 else self.mid
        return self.accent if noise(x // 2, y // 2, seed + 3) > 0.55 else self.mid

    def _chitin(self, x, y, w, h, seed):
        # carapace plates: smooth dark shells with a bright rim, ridged
        band = y % 6
        if band == 0:
            return self.light
        if band == 5:
            return self.dark
        return mix(self.mid, self.light, 0.25) if x % 7 == 3 else self.mid

    def _dots(self, x, y, w, h, seed):
        # cloth scattered with small embroidered dots (the padded set's gold flecks)
        c = self._cloth(x, y, w, h, seed)
        gx, gy = (x + (y // 4 % 2) * 2) % 4, y % 4
        return self.accent if gx == 1 and gy == 1 else c

    def _checker(self, x, y, w, h, seed):
        # a woven check (Fenris trousers)
        c = self.mid if ((x // 2) + (y // 2)) % 2 == 0 else mix(self.mid, self.dark, 0.45)
        return mix(c, self.light, 0.3) if noise(x, y, seed) < 0.06 else c

    def _mottled(self, x, y, w, h, seed):
        # dyed cloth, uneven, with pale flecks (Embla's robes)
        n = noise(x // 3, y // 3, seed)
        c = mix(self.mid, self.dark, 0.5) if n > 0.7 else (mix(self.mid, self.light, 0.35) if n < 0.25 else self.mid)
        return self.accent if noise(x, y, seed + 9) > 0.975 else c

    def _gold(self, x, y, w, h, seed):
        n = noise(x, y, seed)
        return self.light if n < 0.25 else (self.dark if n > 0.85 else self.mid)

    def _gem(self, x, y, w, h, seed):
        cx, cy = (w - 1) / 2, (h - 1) / 2
        d = max(abs(x - cx) / max(1, cx), abs(y - cy) / max(1, cy))
        if x == int(cx) - 1 and y == int(cy) - 1:
            return (255, 255, 255, 255)
        return self.dark if d > 0.8 else (self.mid if d > 0.4 else self.light)


CLEAR = (0, 0, 0, 0)


class ArmorTexture:
    """One equipment layer texture (128x64): paint faces of the armor boxes, then detail them."""

    def __init__(self):
        self.img = Image.new("RGBA", (64 * SCALE, 32 * SCALE), CLEAR)
        self.px = self.img.load()
        self._seed = 1

    # -- low level: pixels in a face --------------------------------------------------------
    def _origin(self, box, side):
        u, v, w, h = face_rect(box, side)
        return u * SCALE, v * SCALE, w * SCALE, h * SCALE

    def put(self, box, side, x, y, color):
        """One pixel at (x, y) pixels from the face's top-left."""
        ox, oy, w, h = self._origin(box, side)
        if 0 <= x < w and 0 <= y < h:
            self.px[ox + x, oy + y] = color

    def get(self, box, side, x, y):
        ox, oy, w, h = self._origin(box, side)
        return self.px[ox + x, oy + y] if 0 <= x < w and 0 <= y < h else CLEAR

    def size(self, box, side):
        _, _, w, h = self._origin(box, side)
        return w, h

    # -- filling --------------------------------------------------------------------------------
    def fill(self, box, sides, mat, y0=0.0, y1=None, x0=0.0, x1=None, bevel=True):
        """Paint sides of a box with a material, between texel rows y0..y1 and columns x0..x1
        (defaults: the whole face). A soft bevel lights the top edge and shades the bottom."""
        if isinstance(sides, str):
            sides = (sides,) if sides not in ("all", "around") else (SIDES if sides == "all" else AROUND)
        for side in sides:
            w, h = self.size(box, side)
            ya, yb = int(y0 * SCALE), int((y1 if y1 is not None else h / SCALE) * SCALE)
            xa, xb = int(x0 * SCALE), int((x1 if x1 is not None else w / SCALE) * SCALE)
            self._seed += 7
            for y in range(max(0, ya), min(h, yb)):
                for x in range(max(0, xa), min(w, xb)):
                    c = mat.at(x - xa, y - ya, xb - xa, yb - ya, self._seed)
                    if bevel and side != "top" and side != "bottom":
                        if y == ya:
                            c = mix(c, mat.light, 0.4)
                        elif y == yb - 1:
                            c = mix(c, mat.dark, 0.45)
                    self.put(box, side, x, y, c)

    def clear(self, box, sides, y0=0.0, y1=None, x0=0.0, x1=None):
        """Cut a hole (the face shows through)."""
        for side in (sides,) if isinstance(sides, str) else sides:
            w, h = self.size(box, side)
            for y in range(int(y0 * SCALE), min(h, int((y1 if y1 is not None else h / SCALE) * SCALE))):
                for x in range(int(x0 * SCALE), min(w, int((x1 if x1 is not None else w / SCALE) * SCALE))):
                    self.put(box, side, x, y, CLEAR)

    # -- details -------------------------------------------------------------------------------
    def band(self, box, sides, y0, y1, mat, bevel=True):
        """A horizontal band round the box (a belt, a hem, a rim)."""
        self.fill(box, sides, mat, y0, y1, bevel=bevel)

    def vband(self, box, sides, x0, x1, mat, y0=0.0, y1=None):
        """A vertical strip (a strap, a seam, a nasal guard)."""
        self.fill(box, sides, mat, y0, y1, x0, x1, bevel=False)

    def rect(self, box, side, x0, y0, x1, y1, mat, bevel=True):
        self.fill(box, (side,), mat, y0, y1, x0, x1, bevel=bevel)

    def rivets(self, box, sides, y, color, every=2.0, x0=0.5, x1=None):
        """A row of rivets (bright dot, dark shadow under) at texel row y."""
        for side in (sides,) if isinstance(sides, str) else sides:
            w, _ = self.size(box, side)
            end = (x1 if x1 is not None else w / SCALE - 0.5)
            x = x0
            while x <= end + 1e-6:
                px, py = int(x * SCALE), int(y * SCALE)
                self.put(box, side, px, py, hexc(color) if isinstance(color, str) else color)
                self.put(box, side, px, py + 1, mix(hexc(color) if isinstance(color, str) else color, (0, 0, 0, 255), 0.6))
                x += every

    def line(self, box, side, x0, y0, x1, y1, color):
        """A 1-pixel line between texel points (stitching, cracks, trims)."""
        c = hexc(color) if isinstance(color, str) else color
        a, b = (x0 * SCALE, y0 * SCALE), (x1 * SCALE, y1 * SCALE)
        n = int(max(abs(b[0] - a[0]), abs(b[1] - a[1]))) + 1
        for i in range(n):
            t = i / max(1, n - 1)
            self.put(box, side, int(round(a[0] + (b[0] - a[0]) * t)), int(round(a[1] + (b[1] - a[1]) * t)), c)

    def stitches(self, box, sides, y, color, every=1.0):
        """Dashed stitching along texel row y."""
        c = hexc(color) if isinstance(color, str) else color
        for side in (sides,) if isinstance(sides, str) else sides:
            w, _ = self.size(box, side)
            for x in range(0, w, int(every * SCALE)):
                self.put(box, side, x, int(y * SCALE), c)

    def shade_sides(self, box, amount=0.18):
        """The sides and back a little darker than the front, so the shape reads."""
        for side, k in (("right", amount), ("left", amount), ("back", amount * 0.6), ("bottom", amount * 1.5)):
            w, h = self.size(box, side)
            for y in range(h):
                for x in range(w):
                    c = self.get(box, side, x, y)
                    if c[3]:
                        self.put(box, side, x, y, mix(c, (0, 0, 0, 255), k) [:3] + (c[3],))

    def save(self, path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        self.img.save(path)
