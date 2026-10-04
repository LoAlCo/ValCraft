"""Draws ValCraft's hand-made 16x16 item textures from the pixel art in tools/item_art/*.txt.

Each texture there is redrawn by hand from Valheim's own icon for the item, in Minecraft's style:

    @ flametal_ore outline=2a0d06      <- texture name; outline: a dark edge added around the art
    r c4381a                           <- palette: one character = one colour (hex)
    R 8e2410
    ................                   <- 16 rows of 16; '.' is transparent
    ...

Optional on the @ line: mirror (rows are the left 8 columns, mirrored), outline=<hex>, base=<template>.

A template is a drawing whose characters are roles (blade edge, grip, ...), shared by items of one
shape that differ in colour, like Valheim's Nord / Thunderblood / Frostfire weapons:

    @@ sword                           <- template: 16 rows, no palette
    ...
    @ iron_sword from=SwordIron base=sword outline=1a1a1e
    e d8dce4                           <- the roles' colours
    ...                                (rows of its own, if any, replace the template's)

  python tools/draw_items.py               writes every texture into the mod's textures/item
  python tools/draw_items.py --sheet DIR   also writes comparison sheets (Valheim icon | drawing),
                                           with the icons from [Debug] ExportIcons (needs Pillow)
"""
import os
import struct
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.join(HERE, "item_art")
OUT = os.path.join(HERE, "..", "fabric", "src", "main", "resources", "assets", "valcraft", "textures", "item")


def hexcolour(h):
    h = h.strip().lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def parse(path):
    """-> list of (name, prefab or None, {(x, y): (r, g, b)})"""
    out = []
    cur = None
    for raw in open(path, encoding="utf-8"):
        if raw.startswith("@@"):
            if cur:
                out.append(finish(cur, path))
            cur = {"name": raw[2:].strip(), "template": True, "opts": {}, "palette": {}, "rows": []}
            continue
        line = raw.rstrip("\n")
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        if line.startswith("@"):
            if cur:
                out.append(finish(cur, path))
            parts = line[1:].split()
            cur = {"name": parts[0], "opts": {}, "palette": {}, "rows": []}
            for p in parts[1:]:
                k, _, v = p.partition("=")
                cur["opts"][k] = v or True
            continue
        if cur is None:
            raise SystemExit(f"{path}: art before any @ line")
        s = line.strip()
        width = 8 if cur["opts"].get("mirror") else 16
        if " " in s:  # palette: "<char> <hex>"
            k, v = s.split()
            if len(k) != 1:
                raise SystemExit(f"{path}: {cur['name']}: bad line {s!r}")
            cur["palette"][k] = hexcolour(v)
        elif len(s) == width:
            cur["rows"].append(s)
        else:
            raise SystemExit(f"{path}: {cur['name']}: row {s!r} isn't {width} wide")
    if cur:
        out.append(finish(cur, path))
    return [d for d in out if d is not None]


TEMPLATES = {}


def finish(cur, path):
    if cur.get("template"):
        if len(cur["rows"]) != 16:
            raise SystemExit(f"{path}: template {cur['name']} has {len(cur['rows'])} rows, not 16")
        TEMPLATES[cur["name"]] = cur["rows"]
        return None
    rows = cur["rows"]
    if not rows and "base" in cur["opts"]:
        if cur["opts"]["base"] not in TEMPLATES:
            raise SystemExit(f"{path}: {cur['name']}: no template {cur['opts']['base']} (define it above with @@)")
        rows = TEMPLATES[cur["opts"]["base"]]
    if len(rows) != 16:
        raise SystemExit(f"{path}: {cur['name']} has {len(rows)} rows, not 16")
    px = {}
    mirror = cur["opts"].get("mirror")
    for y, row in enumerate(rows):
        for x, c in enumerate(row):
            if c == ".":
                continue
            if c not in cur["palette"]:
                raise SystemExit(f"{path}: {cur['name']}: no colour for {c!r}")
            px[(x, y)] = cur["palette"][c]
            if mirror:
                px[(15 - x, y)] = cur["palette"][c]
    if "outline" in cur["opts"]:
        edge = hexcolour(cur["opts"]["outline"])
        out = dict(px)
        for (x, y) in px:
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if (nx, ny) not in px and 0 <= nx < 16 and 0 <= ny < 16:
                    out[(nx, ny)] = edge
        px = out
    return cur["name"], cur["opts"].get("from"), px


def png(path, px):
    rows = b""
    for y in range(16):
        rows += b"\x00" + b"".join(bytes(px[(x, y)]) + b"\xff" if (x, y) in px else b"\x00\x00\x00\x00" for x in range(16))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 16, 16, 8, 6, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


def sheet(out_dir, art_file, drawn, icons):
    from PIL import Image, ImageDraw
    cell = 96
    cols = 4
    n = len(drawn)
    rows = (n + cols - 1) // cols
    img = Image.new("RGB", (cols * cell * 2, rows * (cell + 14)), (70, 70, 70))
    d = ImageDraw.Draw(img)
    for i, (name, prefab, px) in enumerate(drawn):
        x = (i % cols) * cell * 2
        y = (i // cols) * (cell + 14)
        if prefab and os.path.exists(os.path.join(icons, prefab + ".png")):
            ic = Image.open(os.path.join(icons, prefab + ".png")).convert("RGBA").resize((cell, cell), Image.LANCZOS)
            bg = Image.new("RGBA", ic.size, (70, 70, 70, 255))
            bg.alpha_composite(ic)
            img.paste(bg.convert("RGB"), (x, y))
        tile = Image.new("RGB", (16, 16), (90, 90, 90))
        for (px_, py_), c in px.items():
            tile.putpixel((px_, py_), c)
        img.paste(tile.resize((cell, cell), Image.NEAREST), (x + cell, y))
        d.text((x + 2, y + cell), name[:30], fill=(255, 255, 255))
    path = os.path.join(out_dir, os.path.splitext(os.path.basename(art_file))[0] + ".png")
    img.save(path)
    print("sheet", path)


def main():
    icons = None
    if "--sheet" in sys.argv:
        out_dir = sys.argv[sys.argv.index("--sheet") + 1]
        icons = os.environ.get("VALCRAFT_ICONS", os.path.join(os.environ["APPDATA"], "com.kesomannen.gale", "valheim",
                                                             "profiles", "MC-V2", "BepInEx", "ValCraft icons"))
    only = [a for a in sys.argv[1:] if a.endswith(".txt")]
    count = 0
    for f in sorted(os.listdir(ART)):
        if not f.endswith(".txt"):
            continue
        drawn = parse(os.path.join(ART, f))  # every file: later ones may use its templates
        if only and f not in only:
            continue
        for name, _, px in drawn:
            png(os.path.join(OUT, name + ".png"), px)
            count += 1
        if icons:
            sheet(out_dir, f, drawn, icons)
    print(f"wrote {count} textures")


if __name__ == "__main__":
    main()
