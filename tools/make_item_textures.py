"""Draws ValCraft's 16x16 Minecraft-style item textures for Valheim items (original pixel art):
the deer trophy (Eikthyr's offering) and the fuling totem (Yagluth's offering).

  python tools/make_item_textures.py
"""
import os
import struct
import zlib

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fabric", "src", "main", "resources",
                   "assets", "valcraft", "textures", "item")


def png(path, px):
    rows = b""
    for y in range(16):
        rows += b"\x00" + b"".join(bytes(px[(x, y)]) + b"\xff" if (x, y) in px else b"\x00\x00\x00\x00" for x in range(16))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 16, 16, 8, 6, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))
    print("wrote", os.path.normpath(path))


def from_rows(rows, palette, mirror=False):
    """rows: 16 strings (8 wide when mirrored, the left half), palette: char -> colour, '.' empty."""
    px = {}
    for y, row in enumerate(rows):
        for x, c in enumerate(row):
            if c == ".":
                continue
            px[(x, y)] = palette[c]
            if mirror:
                px[(15 - x, y)] = palette[c]
    return px


def outline(px, colour, skip=()):
    """A dark edge around the sprite; colours in `skip` (thin details like quills) get none."""
    out = dict(px)
    for (x, y), c in px.items():
        if c in skip:
            continue
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if (nx, ny) not in px and 0 <= nx < 16 and 0 <= ny < 16:
                out[(nx, ny)] = colour
    return out


# ---- deer trophy: a deer's head with antlers, from the front -----------------------------------

DEER = {
    "a": (214, 196, 160),  # antler
    "A": (160, 138, 104),  # antler shade
    "b": (150, 98, 56),    # fur
    "B": (112, 70, 38),    # fur shade
    "l": (196, 150, 104),  # light face
    "w": (230, 214, 190),  # inner ear
    "e": (24, 16, 12),     # eye
    "n": (44, 30, 24),     # nose
}
DEER_ROWS = [  # left half, mirrored
    "..a.....",
    "..a..a..",
    "a.A..a..",
    ".aA.A...",
    "..aAA...",
    "...aA...",
    "....A.b.",
    ".BbwbbBb",
    "..BbBbbb",
    "....Bebl",
    "....bbbl",
    ".....bll",
    ".....bbl",
    "......bl",
    "......nn",
    ".......n",
]


# ---- fuling totem: a carved bone figurine with a goblin face, quills on its head, arms crossed ---

TOTEM = {
    "b": (232, 218, 176),  # carved bone
    "S": (196, 176, 128),  # bone shade
    "D": (150, 128, 84),   # deep shade
    "e": (40, 28, 20),     # eyes
    "m": (196, 80, 84),    # mouth
    "r": (176, 64, 60),    # red band
    "q": (128, 58, 56),    # quills
}
TOTEM_ROWS = [  # left half, mirrored
    "....q.q.",
    "....q.q.",
    "....q.q.",
    ".....bbb",
    "..SS.bbb",
    "...SSebb",
    ".....bbb",
    ".....Sbm",
    "......Sb",
    ".....bbb",
    "....bSDD",
    "....brrr",
    ".....bbS",
    ".....bb.",
    ".....bS.",
    ".....SD.",
]


def main():
    os.makedirs(OUT, exist_ok=True)
    deer = outline(from_rows(DEER_ROWS, DEER, mirror=True), (40, 26, 16))
    png(os.path.join(OUT, "deer_trophy.png"), deer)
    totem = outline(from_rows(TOTEM_ROWS, TOTEM, mirror=True), (52, 38, 24), skip=(TOTEM["q"],))
    png(os.path.join(OUT, "fuling_totem.png"), totem)


if __name__ == "__main__":
    main()
