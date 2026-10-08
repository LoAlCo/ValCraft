"""Inventory icons for ValCraft's Valheim armor (16x16, Minecraft's style).

A helmet, chestplate or leggings icon has Minecraft's own shape and shading (its iron armor icons,
from the game jar), filled from the set's worn texture (tools/armor_sets.py): the front of the head,
of the body and sleeves, or of the legs, so a chainmail tunic's icon is chainmail with its belt. Pieces
that aren't shaped like Minecraft armor (a crown, a circlet, a hat, a necklace) are Valheim's own icon,
pixelized (tools/pixelize_icons.py).

  python tools/armor_icons.py "<profile>/BepInEx/ValCraft icons"
"""
import io
import os
import sys
import zipfile

from PIL import Image

import armor_sets
import armorpaint as ap

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "fabric", "src", "main", "resources", "assets", "valcraft", "textures", "item")
JAR = os.path.join(os.path.expanduser("~"), ".gradle", "caches", "fabric-loom", "26.3", "minecraft-client.jar")
# Pieces drawn from Valheim's icon instead (prefab -> colours to keep)
FROM_VALHEIM = {"HelmetCrownofValheim": 7, "HelmetDverger": 6, "HelmetFishingHat": 7, "ArmorBerserkerChest": 6}


def vanilla(name):
    with zipfile.ZipFile(JAR) as z:
        return Image.open(io.BytesIO(z.read(f"assets/minecraft/textures/item/{name}.png"))).convert("RGBA")


def lum(c):
    return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]


def front_view(tex, layer_parts):
    """A flat front view (texels, 1 per pixel) of the worn texture's parts: [(box, x, y, flip)]."""
    w = max(x + ap.BOXES[b][2] for b, x, y, f in layer_parts)
    h = max(y + ap.BOXES[b][3] for b, x, y, f in layer_parts)
    view = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    for box, x, y, flip in layer_parts:
        u, v, fw, fh = ap.face_rect(box, "front")
        crop = tex.crop((u * ap.SCALE, v * ap.SCALE, (u + fw) * ap.SCALE, (v + fh) * ap.SCALE)).resize((fw, fh), Image.BOX)
        if flip:
            crop = crop.transpose(Image.FLIP_LEFT_RIGHT)
        view.alpha_composite(crop, (x, y))
    side = None
    return view


def icon(shape, view, box, fallback):
    """Minecraft's icon shape, coloured from the view: the shape's pixel (x, y) inside box (x0, y0,
    x1, y1) takes the view's colour there, shaded the way Minecraft's icon is; where the view is
    empty (an open face, bare skin), the fallback colour."""
    x0, y0, x1, y1 = box
    out = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    inside = [shape.getpixel((x, y)) for y in range(16) for x in range(16) if shape.getpixel((x, y))[3] > 0]
    mean = sum(lum(c) for c in inside) / max(1, len(inside))
    for y in range(16):
        for x in range(16):
            s = shape.getpixel((x, y))
            if s[3] == 0:
                continue
            vx = (x - x0 + 0.5) / (x1 - x0) * view.width
            vy = (y - y0 + 0.5) / (y1 - y0) * view.height
            c = view.getpixel((min(view.width - 1, max(0, int(vx))), min(view.height - 1, max(0, int(vy)))))
            if c[3] < 128:
                c = fallback
            k = max(0.55, min(1.35, lum(s) / max(1.0, mean)))
            if lum(s) < mean * 0.45:  # Minecraft's dark outline
                k = 0.45
            out.putpixel((x, y), tuple(max(0, min(255, int(c[i] * k))) for i in range(3)) + (255,))
    return out


def average(view):
    px = [c for c in view.get_flattened_data() if c[3] > 128]
    if not px:
        return (90, 90, 90, 255)
    return tuple(sum(c[i] for c in px) // len(px) for i in range(3)) + (255,)


def main():
    icons_dir = sys.argv[1] if len(sys.argv) > 1 else None
    shapes = {s: vanilla(f"iron_{s}") for s in ("helmet", "chestplate", "leggings", "boots")}
    made = 0
    for s in armor_sets.SETS:
        top, legs = armor_sets.paint(s)
        pieces = list(s["pieces"])
        if legs is not None and not any(p[0] == "boots" for p in pieces):
            pieces.append(("boots", "-", ""))
        for slot, prefab, name in pieces:
            path = os.path.join(OUT, f"{s['id']}_{slot}.png")
            if prefab in FROM_VALHEIM:
                if not icons_dir:
                    raise SystemExit("pass the Valheim icons folder for " + prefab)
                import pixelize_icons as pi
                pi.png(path, pi.pixelize(os.path.join(icons_dir, prefab + ".png"), FROM_VALHEIM[prefab]))
                made += 1
                continue
            if slot == "helmet":
                # the helmet seen a little from the side: its side behind, its front over it, so an
                # open face shows the inside of the far side, as Minecraft's own helmets do
                u, v, fw, fh = ap.face_rect("head", "right")
                side = top.img.crop((u * ap.SCALE, v * ap.SCALE, (u + fw) * ap.SCALE, (v + fh) * ap.SCALE)).resize((fw, fh), Image.BOX)
                side = Image.eval(side, lambda c: int(c * 0.6))
                view = Image.new("RGBA", (8, 8), (0, 0, 0, 0))
                view.alpha_composite(side.convert("RGBA"), (0, 0))
                view.alpha_composite(front_view(top.img, [("head", 0, 0, False)]), (0, 0))
                im = icon(shapes["helmet"], view, (3, 3, 13, 11), (34, 28, 26, 255))
            elif slot == "chestplate":
                view = front_view(top.img, [("arm", 0, 0, False), ("body", 4, 0, False), ("arm", 12, 0, True)])
                im = icon(shapes["chestplate"], view, (1, 2, 15, 15), average(view))
            elif slot == "boots":
                lower = front_view(top.img, [("leg", 0, 0, False), ("leg", 5, 0, True)]).crop((0, int(armor_sets.BOOT_ROWS), 9, 12))
                im = icon(shapes["boots"], lower, (1, 6, 15, 14), average(lower))
            else:
                view = front_view(legs.img, [("leg", 0, 0, False), ("leg", 4, 0, True)])
                waist = front_view(legs.img, [("body", 0, 0, False)]).crop((0, 8, 8, 12))
                full = Image.new("RGBA", (8, 16), (0, 0, 0, 0))
                full.alpha_composite(waist, (0, 0))
                full.alpha_composite(view, (0, 4))
                im = icon(shapes["leggings"], full, (3, 2, 13, 15), average(view))
            os.makedirs(OUT, exist_ok=True)
            im.save(path)
            made += 1
    print(f"{made} armor icons in {OUT}")


if __name__ == "__main__":
    main()
