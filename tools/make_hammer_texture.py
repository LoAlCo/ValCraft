"""Draws fabric/.../textures/item/build_hammer.png: a 16x16 Minecraft-style tool sprite of
Valheim's build hammer, a wooden mallet: a barrel-shaped wooden head with lighter end grain on
its caps, on a wooden handle. Original art.

  python tools/make_hammer_texture.py
"""
import os
import struct
import zlib

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fabric", "src", "main", "resources",
                   "assets", "valcraft", "textures", "item", "build_hammer.png")

HANDLE, HANDLE_DARK = (125, 86, 50), (88, 58, 32)
# the head's strips across the barrel, top (lit) to bottom
HEAD = [(176, 128, 80), (150, 104, 62), (138, 94, 56), (116, 78, 45), (92, 61, 35)]
CAP = (196, 152, 104)
OUTLINE = (38, 25, 14)


def sprite():
    px = {}
    for t in range(7):  # handle, bottom-left up to the head
        x, y = 2 + t, 13 - t
        px[(x, y)] = HANDLE if t % 3 else HANDLE_DARK
    for x in range(16):  # the mallet head across the handle's top end, all wood
        for y in range(16):
            thick, length = x - y, x + y  # the handle runs along x + y = 15
            if 0 <= thick <= 4 and 10 <= length <= 21:
                shade = thick
                if length in (12, 19):
                    shade = min(4, shade + 1)  # a ridge near each end
                c = HEAD[shade]
                if length == 10:
                    c = CAP if thick <= 3 else HEAD[3]  # front end grain
                px[(x, y)] = c
    outline = {}
    for (x, y) in px:
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if (nx, ny) not in px and 0 <= nx < 16 and 0 <= ny < 16:
                outline[(nx, ny)] = OUTLINE
    px.update(outline)
    return px


def main():
    px = sprite()
    rows = b""
    for y in range(16):
        row = b"\x00"
        for x in range(16):
            c = px.get((x, y))
            row += bytes(c) + b"\xff" if c else b"\x00\x00\x00\x00"
        rows += row

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 16, 16, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    open(OUT, "wb").write(png)
    print("wrote", os.path.normpath(OUT))


if __name__ == "__main__":
    main()
