"""The 3D parts of ValCraft's Valheim armor: what a flat armor texture can't shape (horns, antlers, a
bear's ears and snout, wings, crests, a helm's point, a hat's brim, shoulder plates, fur collars),
built from painted boxes like the 3D weapons (tools/blockmodels.py), and the table the mod reads to
put them on (valcraft/armor_parts.tsv; ArmorPartsLayer).

A part model's 0..16 cube is the armor around the body part it's worn on, centred on it: for a
helmet, the helmet's surface (the head plus Minecraft's armor inflation); -Z (north) faces forward,
+X is the wearer's right, +Y up. A body part is drawn at 0.625 block per 16 units, so the body's
armor is the box x 0..16, y -3.2..19.2, z 3.2..12.8, and an arm's x 4.8..11.2, y -3.2..19.2, z 4.8..11.2.

  python tools/armor_parts.py
"""
import json
import os

import blockmodels as bm
from blockmodels import Mat

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.join(HERE, "..", "fabric", "src", "main", "resources")
ASSETS = os.path.join(RES, "assets", "valcraft")

BRONZE = Mat("metal", "#e0a668", "#b07040", "#6e3e1e")
IRON = Mat("metal", "#d0d4d8", "#8d9399", "#4c5258")
STEEL = Mat("metal", "#8a9096", "#5a6066", "#2c3034")
PALE_STEEL = Mat("metal", "#d8e2ec", "#9aa8b8", "#4a5666")
FLAMETAL = Mat("metal", "#ffc080", "#d06a2c", "#6a2e12")
HORN = Mat("rough", "#f0e6cc", "#cfc09c", "#8a7a58")
BONE = Mat("rough", "#f4ecd8", "#d8ccb0", "#8e8064")
ANTLER = Mat("rough", "#d8c4a0", "#a8906a", "#5e4c34")
FUR_WHITE = Mat("rough", "#ffffff", "#dde0e4", "#9ca0a6")
FUR_GREY = Mat("rough", "#e0dcd4", "#aaa49a", "#6a645c")
FUR_BROWN = Mat("rough", "#7a5a40", "#55392a", "#2a1c12")
NOSE = Mat("plain", "#3a2a22", "#241814", "#100a08")
GOLD = Mat("gold", "#ffe48a", "#e0a838", "#8a5a14")
SHELL = Mat("metal", "#c8d2da", "#6a7480", "#262c34")
ROOT = Mat("wood", "#9c8058", "#6e5636", "#3a2a18")
MOSS = Mat("rough", "#8aa850", "#5e7a34", "#34481a")
HAT = Mat("plain", "#9a8060", "#7a6246", "#4a3a26")
GEM_BLUE = Mat("gem", "#c8f0ff", "#40a8e8", "#1a4a8a", glow=8)
GEM_CYAN = Mat("gem", "#e0fffc", "#40e0d8", "#108a88", glow=12)

PARTS = []  # (armor item, part id, where, boxes)


class Part:
    def __init__(self, armor_item, name, where):
        self.armor_item, self.name, self.where = armor_item, name, where
        self.boxes = []
        PARTS.append(self)

    def box(self, x0, y0, z0, x1, y1, z1, mat):
        self.boxes.append(((x0, y0, z0), (x1, y1, z1), mat))
        return self

    def mirror(self):
        """Every box so far, mirrored to the other side (x -> 16 - x)."""
        for (x0, y0, z0), (x1, y1, z1), m in list(self.boxes):
            self.boxes.append(((16 - x1, y0, z0), (16 - x0, y1, z1), m))
        return self


def cone(p, mat, base=4.0, tiers=3, y=16.0, step=1.6):
    """A helm's point: stacked, shrinking boxes on top of the head."""
    for i in range(tiers):
        half = base / 2 - i * (base / 2) / tiers
        p.box(8 - half, y + i * step, 8 - half, 8 + half, y + (i + 1) * step, 8 + half, mat)


def pauldron(armor_item, mat, trim=None, wide=1.0):
    """Shoulder plates on both arms: a curved plate over the top of the arm."""
    for where in ("RIGHT_ARM", "LEFT_ARM"):
        p = Part(armor_item, f"{armor_item}_{where.lower()}", where)
        p.box(3.2 - wide, 15.5, 3.2 - wide, 12.8 + wide, 19.5, 12.8 + wide, mat)
        p.box(3.6 - wide, 12.5, 3.6 - wide, 12.4 + wide, 15.5, 12.4 + wide, mat)
        if trim:
            p.box(3.0 - wide, 12.2, 3.0 - wide, 13.0 + wide, 12.9, 13.0 + wide, trim)


def build():
    # Viking cap helms: a point on top
    p = Part("bronze_helmet", "bronze_helmet_point", "HEAD")
    cone(p, BRONZE, base=6, tiers=3)
    p = Part("iron_helmet", "iron_helmet_point", "HEAD")
    cone(p, IRON, base=6, tiers=3)
    p = Part("leather_helmet", "leather_helmet_point", "HEAD")
    cone(p, Mat("rough", "#a8764c", "#835634", "#4e2f1b"), base=5, tiers=2)

    # the Drake helmet's horns: out from the sides, then curving up and forward
    p = Part("wolf_helmet", "wolf_helmet_horns", "HEAD")
    p.box(15.5, 9, 6.5, 18.5, 12, 9.5, HORN)
    p.box(17.5, 11, 6.8, 20, 14.5, 9.2, HORN)
    p.box(18.5, 14, 5.5, 20.5, 17.5, 7.5, HORN)
    p.box(19, 17, 4, 20.5, 19.5, 5.5, HORN)
    p.mirror()
    p.box(6.5, 16, 6, 9.5, 17.5, 10, STEEL)  # a ridge over the crown

    # Flametal: antlers of flametal-shod horn, branching up and back
    p = Part("flametal_helmet", "flametal_helmet_antlers", "HEAD")
    p.box(13.5, 14, 8, 15.5, 18, 10, ANTLER)
    p.box(14.5, 17.5, 8.5, 16.5, 23, 10.5, ANTLER)
    p.box(16, 21, 9, 18, 25, 11, ANTLER)
    p.box(13, 20, 10, 15, 23, 12, ANTLER)
    p.box(17.5, 24, 9.5, 19, 27, 11, FLAMETAL)
    p.box(14.8, 22.5, 8.8, 16.2, 26, 10.2, FLAMETAL)
    p.mirror()

    # the Caller's headdress: a moose skull (its snout over the brow) and broad palmate antlers
    p = Part("caller_helmet", "caller_helmet_skull", "HEAD")
    p.box(4.5, 12, -4, 11.5, 16.5, 1, BONE)
    p.box(5.5, 11, -6.5, 10.5, 14.5, -3.5, BONE)
    p.box(6.5, 11.5, -7, 7.5, 12.5, -6.4, NOSE)
    p.box(8.5, 11.5, -7, 9.5, 12.5, -6.4, NOSE)
    p.box(14, 14, 6, 17, 16.5, 9, ANTLER)
    p.box(16.5, 15, 3, 24, 16.5, 11, ANTLER)       # the palm
    p.box(23, 15.5, 2, 25, 20, 4, ANTLER)          # tines along its edge
    p.box(23, 15.5, 6, 25, 21, 8, ANTLER)
    p.box(22, 15.5, 9.5, 24, 19.5, 11.5, ANTLER)
    p.box(4.5, 15, 3, 7, 16.5, 11, ANTLER)         # mirrored later: keep this one off the mirror
    p.boxes.pop()
    p.mirror()

    # the bear's head: ears and a snout over the brow, its nose and fangs
    p = Part("bear_helmet", "bear_helmet_head", "HEAD")
    p.box(1, 14.5, 9, 4.5, 18.5, 12, FUR_BROWN)
    p.box(11.5, 14.5, 9, 15, 18.5, 12, FUR_BROWN)
    p.box(4, 12, -4.5, 12, 16.5, 1, FUR_BROWN)
    p.box(6, 14.5, -5.2, 10, 16.5, -4.4, NOSE)
    p.box(4.5, 10.5, -4.2, 5.5, 12, -3.4, BONE)
    p.box(10.5, 10.5, -4.2, 11.5, 12, -3.4, BONE)

    # the Protector's helm: steel wings swept back from the temples
    p = Part("protector_helmet", "protector_helmet_wings", "HEAD")
    p.box(15.5, 8, 7, 17, 13, 12, PALE_STEEL)
    p.box(16, 11, 9, 17.5, 17, 14, PALE_STEEL)
    p.box(16.3, 14, 11.5, 18, 21, 15.5, PALE_STEEL)
    p.box(16.6, 18, 14, 18.4, 24, 16.5, GOLD)
    p.mirror()
    p.box(7, 16, 0, 9, 18.5, 14, GOLD)  # a gilt crest from brow to nape

    # root mask: branches growing up from the crown, moss on them
    p = Part("root_helmet", "root_helmet_branches", "HEAD")
    p.box(3, 16, 7, 4.5, 21, 8.5, ROOT)
    p.box(1.5, 20, 7.5, 3, 24, 9, ROOT)
    p.box(4, 20.5, 6.5, 5.5, 23, 8, ROOT)
    p.box(2.5, 18, 6.5, 4.5, 19, 8.5, MOSS)
    p.mirror()
    p.box(7.2, 16, 9, 8.8, 20, 10.6, ROOT)

    # carapace helm: a crest of chitin spikes from brow to nape
    p = Part("carapace_helmet", "carapace_helmet_crest", "HEAD")
    for i, (z, h) in enumerate(((1, 2.5), (4, 4), (7, 5), (10, 4), (13, 2.5))):
        p.box(7, 16, z, 9, 16 + h, z + 2.2, SHELL)

    # vilebone visage: tusks curling down from the jaw
    p = Part("vilebone_helmet", "vilebone_helmet_tusks", "HEAD")
    p.box(1, 1, -2.5, 3, 7, -0.5, BONE)
    p.box(0.5, -2, -3.5, 2.5, 1.5, -1.5, BONE)
    p.mirror()

    # lox fur hood: a shaggy fringe over the brow
    p = Part("lox_helmet", "lox_helmet_fringe", "HEAD")
    p.box(-0.5, 11, -1, 16.5, 16.5, 3, Mat("rough", "#f0b860", "#c88838", "#7a4e18"))

    # the fishing hat: a wide brim and a low crown with its band (the bobber is on the texture)
    p = Part("fishing_helmet", "fishing_helmet_brim", "HEAD")
    p.box(-3.5, 11, -3.5, 19.5, 12, 19.5, HAT)
    p.box(1, 16, 1, 15, 20, 15, HAT)
    p.box(0.8, 16, 0.8, 15.2, 17.5, 15.2, Mat("plain", "#c04a3a", "#a83a2a", "#5a1c14"))

    # the Crown of Valheim: tall gilt points and its blue stone
    p = Part("crown_helmet", "crown_helmet_points", "HEAD")
    for x in (0, 7, 14):
        p.box(x, 15, -0.5, x + 2, 20, 1.5, GOLD)
        p.box(x, 15, 14.5, x + 2, 20, 16.5, GOLD)
    for z in (5, 10):
        p.box(-0.5, 15, z, 1.5, 19, z + 2, GOLD)
        p.box(14.5, 15, z, 16.5, 19, z + 2, GOLD)
    p.box(6.5, 11, -1.2, 9.5, 14, -0.4, GEM_BLUE)

    p = Part("dverger_helmet", "dverger_helmet_stone", "HEAD")
    p.box(6.5, 10.5, -0.8, 9.5, 13.5, 0, GEM_CYAN)

    # shoulder plates
    pauldron("bronze_chestplate", BRONZE, trim=Mat("metal", "#c88850", "#94582c", "#5a3214"))
    pauldron("padded_chestplate", STEEL, trim=IRON)
    pauldron("flametal_chestplate", STEEL, trim=FLAMETAL, wide=1.4)
    pauldron("carapace_chestplate", SHELL, trim=Mat("plain", "#c04a3a", "#a83a2a", "#5a1c14"), wide=1.2)
    pauldron("protector_chestplate", PALE_STEEL, trim=GOLD, wide=1.6)
    pauldron("iron_chestplate", Mat("rough", "#9a6a44", "#7a4e30", "#4e2f1b"), trim=IRON, wide=0.4)

    # fur mantles over the shoulders
    for item, fur in (("wolf_chestplate", FUR_GREY), ("protector_chestplate", FUR_WHITE), ("vanguard_chestplate", FUR_WHITE),
                      ("bear_chestplate", FUR_BROWN), ("fenris_chestplate", Mat("rough", "#6a6460", "#47423e", "#22201e"))):
        p = Part(item, f"{item}_mantle", "BODY")
        p.box(-1.5, 15.5, 1.5, 17.5, 20.5, 14.5, fur)
        p.box(-0.5, 13, 2.5, 16.5, 15.5, 13.5, fur)


# The armor's own surface round each body part (the worn texture is drawn there), per axis: a part's
# face lying on it would flicker against it, so it's moved just outside.
SURFACE = {
    "HEAD": ((0.0, 16.0), (0.0, 16.0), (0.0, 16.0)),
    "BODY": ((0.0, 16.0), (-3.2, 19.2), (3.2, 12.8)),
    "RIGHT_ARM": ((3.2, 12.8), (-3.2, 19.2), (3.2, 12.8)),
    "LEFT_ARM": ((3.2, 12.8), (-3.2, 19.2), (3.2, 12.8)),
}
EPS = 0.15


def off_surface(p):
    planes = SURFACE[p.where]
    out = []
    for lo, hi, m in p.boxes:
        lo, hi = list(lo), list(hi)
        for ax in range(3):
            near, far = planes[ax]
            centre = (near + far) / 2
            for v in (lo, hi):
                for plane in (near, far):
                    if abs(v[ax] - plane) < 1e-3:
                        v[ax] = plane + (EPS if plane > centre else -EPS)
        out.append((tuple(lo), tuple(hi), m))
    p.boxes = out


def separate(p):
    """Parts touching each other face to face: the later one is pushed out a little there (as blockmodels does)."""
    model = bm.Model(p.name)
    model.boxes = p.boxes
    model.separate()
    p.boxes = model.boxes


def write():
    rows = ["# Generated by tools/armor_parts.py: Valheim armor item, its 3D part's model, where it's worn"]
    for p in PARTS:
        off_surface(p)
        separate(p)
        size, placed, tex = bm.pack(p.boxes)
        k = 16 / size
        elements = []
        for i, ((x0, y0, z0), (x1, y1, z1), mat) in enumerate(p.boxes):
            el = {"from": [round(x0, 3), round(y0, 3), round(z0, 3)], "to": [round(x1, 3), round(y1, 3), round(z1, 3)], "faces": {}}
            for f in bm.FACES:
                x, y, fw, fh = placed[(i, f)]
                el["faces"][f] = {"uv": [x * k, y * k, (x + fw) * k, (y + fh) * k], "texture": "#0"}
            if mat.glow:
                el["light_emission"] = mat.glow
            elements.append(el)
        lo = min(min(e["from"]) for e in elements)
        hi = max(max(e["to"]) for e in elements)
        if lo < -16 or hi > 32:
            raise SystemExit(f"{p.name}: parts reach {lo:.1f}..{hi:.1f}, outside the -16..32 Minecraft allows")
        model = {"texture_size": [size, size], "textures": {"0": f"valcraft:item/armor3d/{p.name}", "particle": f"valcraft:item/armor3d/{p.name}"},
                 "elements": elements}
        path = os.path.join(ASSETS, "models", "item", "armor3d", p.name + ".json")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        json.dump(model, open(path, "w", encoding="utf-8", newline="\n"), indent=1)
        bm.write_png(os.path.join(ASSETS, "textures", "item", "armor3d", p.name + ".png"), tex)
        path = os.path.join(ASSETS, "items", "armor3d", p.name + ".json")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        json.dump({"model": {"type": "minecraft:model", "model": f"valcraft:item/armor3d/{p.name}"}}, open(path, "w", encoding="utf-8", newline="\n"), indent=2)
        rows.append(f"{p.armor_item}\tarmor3d/{p.name}\t{p.where}")
    open(os.path.join(RES, "valcraft", "armor_parts.tsv"), "w", encoding="utf-8", newline="\n").write("\n".join(rows) + "\n")
    print(f"{len(PARTS)} armor parts")


if __name__ == "__main__":
    build()
    write()
