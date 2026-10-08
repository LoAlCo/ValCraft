"""Valheim's armor sets as Minecraft armor: the worn textures (tools/armorpaint.py), the item table,
item models, names and loot lines, and a preview of each set worn (front and back).

    python tools/armor_sets.py              everything
    python tools/armor_sets.py --preview    only the previews (tools/armor_preview/)

Each set is drawn from the Valheim armor as worn: its helmet (or hood, mask, headdress), body and
sleeves, and its legs with the boots (Valheim's legs piece covers the feet; Minecraft's leggings
layer does too). Stats come from Valheim's (armor, durability), mapped to Minecraft's scale.
"""
import json
import math
import os
import sys

from PIL import Image

import armorpaint as ap
from armorpaint import Mat

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..")
RES = os.path.join(ROOT, "fabric", "src", "main", "resources")
ASSETS = os.path.join(RES, "assets", "valcraft")
PREVIEW = os.path.join(HERE, "armor_preview")


# ---- materials -------------------------------------------------------------------------------
def M(style, light, mid, dark, accent=None, scale=1.0):
    return Mat(style, light, mid, dark, accent, scale)


LEATHER = M("leather", "#9a6a44", "#7a4e30", "#4e2f1b")
DARK_LEATHER = M("leather", "#6e4a33", "#4f3322", "#2e1d13")
BELT = M("leather", "#6b4528", "#4a2e1a", "#25160c")
BUCKLE = M("plate", "#d8d2c0", "#a8a090", "#605848")
BOOT = M("leather", "#5c3c26", "#432a1a", "#24160d")
LINEN = M("cloth", "#cdbf9e", "#ad9f80", "#7c705a")
ROPE = M("cloth", "#b39a6a", "#8c7650", "#5a4a30")


def metal(light, mid, dark):
    return M("plate", light, mid, dark)


BRONZE = metal("#e0a668", "#b07040", "#6e3e1e")
IRON = metal("#c8ccd0", "#8d9399", "#4c5258")
DARK_STEEL = metal("#8a9096", "#5a6066", "#2c3034")
GOLD = M("gold", "#ffe48a", "#e0a838", "#8a5a14")
FLAMETAL = metal("#ffb070", "#d06a2c", "#6a2e12")


# ---- helmets -----------------------------------------------------------------------------
def nasal_helm(t, mat, rim):
    """A Viking cap helm with a nasal guard: covers the top, back and sides; the face shows."""
    t.fill("head", ("top", "back", "right", "left"), mat, 0, 6)
    t.fill("head", "front", mat, 0, 3)
    t.band("head", ap.AROUND, 2.5, 3.5, rim)
    t.vband("head", "front", 3.5, 4.5, rim, 3, 6.5)
    t.rivets("head", ap.AROUND, 3.0, "#f0e8d0", every=1.5)
    t.shade_sides("head")


def hood(t, mat, rim=None, open_rows=(2.5, 7.5), open_cols=(1.5, 6.5)):
    """A hood: all round the head but an opening for the face."""
    t.fill("head", ("top", "back", "right", "left"), mat)
    t.fill("head", "front", mat)
    t.clear("head", "front", open_rows[0], open_rows[1], open_cols[0], open_cols[1])
    if rim:
        t.rect("head", "front", open_cols[0] - 0.5, open_rows[0] - 0.5, open_cols[1] + 0.5, open_rows[0], rim, bevel=False)
        t.vband("head", "front", open_cols[0] - 0.5, open_cols[0], rim, open_rows[0], open_rows[1])
        t.vband("head", "front", open_cols[1], open_cols[1] + 0.5, rim, open_rows[0], open_rows[1])
    t.shade_sides("head")


def closed_helm(t, mat, visor, slit_row=3.5, cheeks=None):
    """A full helm: the face covered but for an eye slit and breathing holes."""
    t.fill("head", ("top", "back", "right", "left", "front"), mat)
    t.rect("head", "front", 0.5, slit_row - 1, 7.5, slit_row, visor, bevel=False)
    t.clear("head", "front", 1.5, slit_row, 6.5, slit_row + 1)
    t.vband("head", "front", 3.5, 4.5, visor, slit_row - 1, 8)
    for y in (slit_row + 2, slit_row + 3):
        for x in (2, 2.5, 5.5, 6):
            t.put("head", "front", int(x * 2), int(y * 2), (20, 20, 20, 255))
    if cheeks:
        t.fill("head", ("right", "left"), cheeks, 4, 8)
    t.shade_sides("head")


# ---- bodies ---------------------------------------------------------------------------------
def tunic(t, mat, belt=BELT, hem=None, collar=None, sleeves=None, sleeve_len=12):
    t.fill("body", ap.AROUND, mat)
    t.fill("body", ("top", "bottom"), mat)
    t.band("body", ap.AROUND, 8, 9, belt)
    t.rect("body", "front", 3.5, 8, 4.5, 9, BUCKLE)
    if hem:
        t.band("body", ap.AROUND, 11, 12, hem)
    if collar:
        t.rect("body", "front", 2, 0, 6, 1, collar)
        t.line("body", "front", 4, 1, 4, 3.5, "#2a1a10")
    arm = sleeves or mat
    t.fill("arm", ap.AROUND, arm, 0, sleeve_len)
    t.fill("arm", "top", arm)
    t.shade_sides("body")
    t.shade_sides("arm")


def pauldrons(t, mat, rows=4, rivet="#f0e8d0"):
    t.fill("arm", ("top",), mat)
    t.fill("arm", ap.AROUND, mat, 0, rows)
    t.band("arm", ap.AROUND, rows - 0.5, rows, M("plain", "#000000", "#2a2a2a", "#000000"), bevel=False)
    if rivet:
        t.rivets("arm", ap.AROUND, 1.0, rivet, every=1.5)


def bracers(t, mat, y0=8, y1=11.5):
    t.fill("arm", ap.AROUND, mat, y0, y1)
    t.band("arm", ap.AROUND, y0, y0 + 0.5, BELT, bevel=False)


# ---- legs (leggings layer) -------------------------------------------------------------------
def trousers(t, mat, boots=BOOT, boot_top=8.5, belt=BELT, wraps=None, greave=None, greave_rows=(5, 9)):
    t.fill("leg", ap.AROUND, mat, 0, boot_top)
    t.fill("leg", "top", mat)
    t.fill("leg", ap.AROUND, boots, boot_top, 12)
    t.fill("leg", "bottom", boots)
    t.band("leg", ap.AROUND, boot_top, boot_top + 0.5, M("plain", "#000000", "#1a120c", "#000000"), bevel=False)
    if wraps:
        for y in range(int(boot_top) + 1, 12, 1):
            t.line("leg", "front", 0, y, 4, y - 0.5, wraps)
            t.line("leg", "right", 0, y - 0.5, 4, y, wraps)
            t.line("leg", "left", 0, y - 0.5, 4, y, wraps)
    if greave:
        t.rect("leg", "front", 0.5, greave_rows[0], 3.5, greave_rows[1], greave)
        t.rivets("leg", "front", greave_rows[0] + 0.5, "#f0e8d0", every=2, x0=1)
    # the waist (the leggings layer's body box): belt and the top of the trousers
    t.fill("body", ap.AROUND, mat, 9, 12)
    t.band("body", ap.AROUND, 8.5, 9.5, belt)
    t.rect("body", "front", 3.5, 8.5, 4.5, 9.5, BUCKLE)
    t.shade_sides("leg")
    t.shade_sides("body")


# ---- the sets -------------------------------------------------------------------------------
SETS = []


def armor_set(set_id, name_hint, valheim, sound="leather", repair="leather"):
    """valheim: (Valheim armor per piece, durability) for the set; the Valheim prefabs and names go in pieces."""
    def wrap(fn):
        SETS.append({"id": set_id, "hint": name_hint, "armor": valheim[0], "durability": valheim[1], "sound": sound, "repair": repair, "paint": fn,
                     "pieces": fn.pieces})
        return fn
    return wrap


def pieces(*p):
    """(slot, Valheim prefab, English name): helmet, chestplate, leggings."""
    def wrap(fn):
        fn.pieces = p
        return fn
    return wrap


@armor_set("leather", "Leather", (2, 400))
@pieces(("helmet", "HelmetLeather", "Leather Helmet"), ("chestplate", "ArmorLeatherChest", "Leather Tunic"), ("leggings", "ArmorLeatherLegs", "Leather Trousers"))
def leather(t, legs):
    # from Valheim's: a studded leather vest over short grey sleeves (bare forearms), a white kilt
    # under the belt, grey trousers, the shins wrapped in orange-brown leather
    vest = M("leather", "#a8704a", "#86523a", "#4e2f1b")
    grey = M("cloth", "#9a948c", "#7a746c", "#4a4642")
    if legs:
        trousers(t, grey, boots=M("leather", "#c87a40", "#a05a28", "#5a3014"), boot_top=7, wraps="#5a3014")
        return
    nasal_helm(t, M("leather", "#a8764c", "#835634", "#4e2f1b"), M("leather", "#d08850", "#a86430", "#5a3014"))
    t.stitches("head", ap.AROUND, 1.5, "#c49a6c", every=1.0)
    t.fill("body", ap.AROUND, vest, 0, 8.5)
    t.fill("body", ("top",), vest)
    t.fill("body", ap.AROUND, M("cloth", "#f0ece4", "#d8d4cc", "#9a968e"), 9.5, 12)
    t.band("body", ap.AROUND, 8.5, 9.5, BELT)
    t.rect("body", "front", 3.25, 8.5, 4.75, 9.5, BUCKLE)
    for y in (2, 4, 6):
        t.rivets("body", ("front", "back"), y, "#e0c8a0", every=2, x0=1, x1=7)
    t.line("body", "front", 4, 0, 4, 8.5, "#3a2414")
    t.fill("arm", ("top",), grey)
    t.fill("arm", ap.AROUND, grey, 0, 4)
    t.shade_sides("body")
    t.shade_sides("arm")


@armor_set("troll", "Troll Leather", (6, 500))
@pieces(("helmet", "HelmetTrollLeather", "Troll Leather Hood"), ("chestplate", "ArmorTrollLeatherChest", "Troll Leather Tunic"), ("leggings", "ArmorTrollLeatherLegs", "Troll Leather Trousers"))
def troll(t, legs):
    # from Valheim's: troll hide dyed blue: a hood, a short-sleeved tunic, a white kilt under the belt,
    # blue trousers, boots strapped in brown
    hide = M("leather", "#5a9aa8", "#3e7684", "#1e3e48")
    if legs:
        trousers(t, hide, boots=M("leather", "#4a8090", "#346070", "#183038"), boot_top=8, wraps="#6a4428")
        t.band("leg", ap.AROUND, 8, 8.75, M("leather", "#8a5a34", "#6a4428", "#3a2414"))
        return
    hood(t, hide, M("leather", "#2e5664", "#1e3e48", "#0e2028"))
    t.fill("body", ap.AROUND, hide, 0, 8.5)
    t.fill("body", ("top",), hide)
    t.fill("body", ap.AROUND, M("cloth", "#f0ece4", "#d8d4cc", "#9a968e"), 9.5, 12)
    t.band("body", ap.AROUND, 8.5, 9.5, BELT)
    t.rect("body", "front", 3.25, 8.5, 4.75, 9.5, BUCKLE)
    t.stitches("body", ap.AROUND, 4, "#9ad0dc", every=1.5)
    t.fill("arm", ("top",), hide)
    t.fill("arm", ap.AROUND, hide, 0, 6)
    t.shade_sides("body")
    t.shade_sides("arm")


@armor_set("bronze", "Bronze", (8, 1000), sound="iron", repair="iron")
@pieces(("helmet", "HelmetBronze", "Bronze Helmet"), ("chestplate", "ArmorBronzeChest", "Bronze Plate Tunic"), ("leggings", "ArmorBronzeLegs", "Bronze Plate Leggings"))
def bronze(t, legs):
    if legs:
        trousers(t, LEATHER, greave=BRONZE, greave_rows=(4, 9))
        t.rect("leg", "front", 0.5, 0.5, 3.5, 3.5, BRONZE)
        t.rivets("leg", "front", 1, "#ffe0a0", every=2, x0=1)
        return
    nasal_helm(t, BRONZE, M("plate", "#c88850", "#94582c", "#5a3214"))
    # small bronze plates riveted onto a leather tunic, in rows
    tunic(t, LEATHER, hem=DARK_LEATHER, sleeve_len=6)
    for row in range(4):
        y = 1 + row * 1.75
        for side in ("front", "back"):
            for col in range(4):
                t.rect("body", side, 0.25 + col * 1.9, y, 0.25 + col * 1.9 + 1.6, y + 1.5, BRONZE)
    for side in ("right", "left"):
        for row in range(4):
            t.rect("body", side, 0.25, 1 + row * 1.75, 3.75, 2.5 + row * 1.75, BRONZE)
    pauldrons(t, BRONZE, rivet="#ffe0a0")


@armor_set("root", "Root", (8, 800))
@pieces(("helmet", "HelmetRoot", "Root Mask"), ("chestplate", "ArmorRootChest", "Root Harnesk"), ("leggings", "ArmorRootLegs", "Root Leggings"))
def root(t, legs):
    roots = M("root", "#9c8058", "#6e5636", "#3a2a18", accent="#5e7a34")
    if legs:
        trousers(t, DARK_LEATHER, boots=M("leather", "#4a3a26", "#33281a", "#1a140c"), wraps="#6e5636")
        t.fill("leg", ("front", "right"), roots, 1, 7)
        return
    # a carved wooden mask with eye holes, roots round the back of the head
    t.fill("head", ("top", "back", "right", "left"), roots)
    wood = M("leather", "#a88a5c", "#86683e", "#4c3820")
    t.fill("head", "front", wood)
    t.clear("head", "front", 1.5, 3, 3, 4)
    t.clear("head", "front", 5, 3, 6.5, 4)
    t.line("head", "front", 4, 4, 4, 6.5, "#4c3820")
    t.line("head", "front", 2, 6.5, 6, 6.5, "#3a2a18")
    t.vband("head", "front", 0, 0.5, M("plain", "#5e7a34", "#5e7a34", "#3e5220"))
    t.shade_sides("head")
    tunic(t, DARK_LEATHER, sleeve_len=8)
    t.fill("body", ("front", "back"), roots, 0, 8)
    t.fill("body", ("right", "left"), roots, 1, 7)
    pauldrons(t, roots, rows=5, rivet=None)


@armor_set("iron", "Iron", (14, 1000), sound="iron", repair="iron")
@pieces(("helmet", "HelmetIron", "Iron Helmet"), ("chestplate", "ArmorIronChest", "Iron Scale Mail"), ("leggings", "ArmorIronLegs", "Iron Greaves"))
def iron(t, legs):
    chain = M("chain", "#c8ccd0", "#7c8288", "#363a3e")
    if legs:
        trousers(t, M("cloth", "#4a4e56", "#363a42", "#1e2026"), greave=IRON, greave_rows=(4.5, 9.5))
        t.fill("leg", ("right", "left"), chain, 0, 5)
        return
    nasal_helm(t, IRON, DARK_STEEL)
    t.fill("head", ("right", "left", "back"), chain, 6, 8)  # an aventail of mail
    tunic(t, chain, hem=chain, sleeve_len=8)
    t.band("body", ap.AROUND, 10, 12, chain)
    # leather straps crossing the chest and the shoulder pads
    t.line("body", "front", 0, 0, 8, 7, "#4a2e1a")
    t.line("body", "front", 0.5, 0, 8, 6.5, "#6b4528")
    t.line("body", "back", 0, 7, 8, 0, "#4a2e1a")
    pauldrons(t, LEATHER, rows=3, rivet="#c8ccd0")
    bracers(t, LEATHER)


@armor_set("fenris", "Fenris", (10, 1000))
@pieces(("helmet", "HelmetFenring", "Fenris Hood"), ("chestplate", "ArmorFenringChest", "Fenris Coat"), ("leggings", "ArmorFenringLegs", "Fenris Leggings"))
def fenris(t, legs):
    # from Valheim's: a dark grey hooded mantle, a dark ragged tunic with a brown belt; checked dark
    # trousers into brown fur-topped boots (a cyan stone on them)
    coat = M("leather", "#4a4c50", "#323438", "#18191c")
    mantle = M("fur", "#5a5c60", "#3a3c40", "#1a1b1e", accent="#7a7c80")
    if legs:
        trousers(t, M("checker", "#4a4c50", "#2e3034", "#141518"), boots=M("leather", "#6a4a34", "#4a3222", "#241810"), boot_top=8)
        t.band("leg", ap.AROUND, 8, 9, mantle, bevel=False)
        t.rect("leg", "front", 1.5, 9.5, 2.5, 10.5, M("gem", "#c0fff8", "#40e0d8", "#108a88"), bevel=False)
        return
    hood(t, mantle, coat, open_rows=(3, 8), open_cols=(1, 7))
    tunic(t, coat, belt=BELT, sleeve_len=12)
    t.fill("body", ap.AROUND, mantle, 0, 3)
    t.fill("arm", ("top",), mantle)
    t.fill("arm", ap.AROUND, mantle, 0, 3)
    for side in ap.AROUND:
        w, h = t.size("body", side)
        for x in range(0, w, 3):
            t.put("body", side, x, h - 1, (0, 0, 0, 0))


@armor_set("wolf", "Wolf", (20, 1000), sound="iron", repair="iron")
@pieces(("helmet", "HelmetDrake", "Drake Helmet"), ("chestplate", "ArmorWolfChest", "Wolf Hide Chestpiece"), ("leggings", "ArmorWolfLegs", "Wolf Hide Trousers"))
def wolf(t, legs):
    fur = M("fur", "#d8d4cc", "#aaa49a", "#6a645c", accent="#f0ece4")
    chain = M("chain", "#c0c6cc", "#76808a", "#323840")
    if legs:
        trousers(t, M("leather", "#7a5236", "#5a3a24", "#2e1c10"), boots=fur, boot_top=7.5, wraps="#5a3a24")
        return
    # the Drake helmet: a dark steel full helm with cheek guards (its horns are the 3D part)
    closed_helm(t, DARK_STEEL, IRON, cheeks=M("plate", "#7a8088", "#545a62", "#2a2e34"))
    t.rivets("head", ap.AROUND, 1, "#c8ccd0", every=2)
    tunic(t, chain, hem=chain, sleeve_len=8)
    t.fill("body", ap.AROUND, fur, 0, 2.5)          # wolf pelt over the shoulders
    t.line("body", "front", 1, 3, 4, 7, "#3a2414")
    t.line("body", "front", 7, 3, 4, 7, "#3a2414")
    pauldrons(t, fur, rows=4, rivet=None)
    bracers(t, LEATHER)


@armor_set("lox", "Lox Fur", (16, 500))
@pieces(("helmet", "HelmetLox", "Lox Fur Hood"), ("chestplate", "ArmorLoxChest", "Lox Fur Jacket"), ("leggings", "ArmorLoxLegs", "Lox Fur Trousers"))
def lox(t, legs):
    fur = M("fur", "#f0b860", "#c88838", "#7a4e18", accent="#ffd890")
    if legs:
        trousers(t, fur, boots=M("fur", "#a87030", "#7a4e18", "#442a0c"), boot_top=8, belt=BELT)
        return
    hood(t, fur, M("fur", "#c88838", "#a06a28", "#5a3a10"), open_rows=(3, 8), open_cols=(1.5, 6.5))
    tunic(t, fur, belt=BELT, sleeve_len=12)


@armor_set("padded", "Padded", (26, 1000), sound="iron", repair="iron")
@pieces(("helmet", "HelmetPadded", "Padded Helmet"), ("chestplate", "ArmorPaddedCuirass", "Padded Cuirass"), ("leggings", "ArmorPaddedGreaves", "Padded Greaves"))
def padded(t, legs):
    # from Valheim's: a grey quilted coif, a dark navy padded coat scattered with gold flecks and
    # its ragged skirt, brown belt and bracers; grey quilted trousers banded in orange, brown boots
    navy = M("dots", "#3a5274", "#22344e", "#101a28", accent="#d8a040")
    coif = M("quilt", "#c8c8c4", "#9a9a96", "#5a5a58", scale=0.8)
    if legs:
        trousers(t, M("quilt", "#b8b8b4", "#8a8a86", "#4a4a48", scale=0.8), boots=BOOT, boot_top=9)
        for y in (3, 6):
            t.band("leg", ap.AROUND, y, y + 0.75, M("cloth", "#e8984a", "#c87a30", "#7a4414"), bevel=False)
        t.fill("body", ap.AROUND, navy, 9.5, 12)
        return
    t.fill("head", ap.SIDES, coif)
    t.clear("head", "front", 1.5, 3, 6.5, 6.5)
    t.band("head", ap.AROUND, 0, 1, M("quilt", "#d8d8d4", "#a8a8a4", "#6a6a68"), bevel=False)
    t.shade_sides("head")
    tunic(t, navy, belt=BELT, sleeves=navy, sleeve_len=12)
    for side in ap.AROUND:
        w, h = t.size("body", side)
        for x in range(0, w, 3):
            t.put("body", side, x, h - 1, (0, 0, 0, 0))
    bracers(t, M("leather", "#8a5a3a", "#6a4428", "#3a2414"), 7, 12)


@armor_set("eitr", "Eitr-weave", (16, 1000))
@pieces(("helmet", "HelmetMage", "Eitr-weave Hood"), ("chestplate", "ArmorMageChest", "Eitr-weave Robe"), ("leggings", "ArmorMageLegs", "Eitr-weave Trousers"))
def eitr(t, legs):
    # from Valheim's: a red hood, a dark green fur collar over the shoulders, a long red robe with
    # white trims and a dark front; red trousers under it, brown boots
    red = M("cloth", "#c8302a", "#a01e1a", "#540c0a")
    fur = M("fur", "#3a5a40", "#22382a", "#0e1a12", accent="#5a7a5a")
    trim = M("plain", "#f0ece4", "#d8d4cc", "#9a968e")
    if legs:
        trousers(t, red, boots=BOOT, boot_top=9)
        t.vband("leg", "front", 1.5, 2.5, M("cloth", "#3a2a2a", "#2a1a1a", "#140a0a"), 0, 9)
        return
    hood(t, red, M("cloth", "#e0443a", "#c02a24", "#701410"), open_rows=(3, 8), open_cols=(1.5, 6.5))
    tunic(t, red, belt=M("leather", "#4a3028", "#2e1c16", "#160c08"), sleeve_len=12)
    t.fill("body", "front", M("cloth", "#4a3434", "#2e1e1e", "#160c0c"), 2, 12, 2.5, 5.5)
    t.vband("body", "front", 2, 2.5, trim, 2, 12)
    t.vband("body", "front", 5.5, 6, trim, 2, 12)
    t.fill("body", ap.AROUND, fur, 0, 2.5)
    t.fill("arm", ("top",), fur)
    t.fill("arm", ap.AROUND, fur, 0, 2.5)
    t.band("arm", ap.AROUND, 10.5, 11.25, trim, bevel=False)


@armor_set("carapace", "Carapace", (32, 1200), sound="iron", repair="netherite")
@pieces(("helmet", "HelmetCarapace", "Carapace Helmet"), ("chestplate", "ArmorCarapaceChest", "Carapace Breastplate"), ("leggings", "ArmorCarapaceLegs", "Carapace Greaves"))
def carapace(t, legs):
    # from Valheim's: plates of dark blue-green chitin flecked red, over brown leather; a grey-blue
    # helm with a white crest (3D); brown trousers, chitin sabatons
    shell = M("chitin", "#5a7a88", "#34505e", "#16262e")
    leather = M("leather", "#8a5a3a", "#6a4428", "#3a2414")
    def specks(box, sides, y0, y1):
        for side in sides:
            w, h = t.size(box, side)
            for y in range(int(y0 * 2), min(h, int(y1 * 2))):
                for x in range(w):
                    if ap.noise(x, y, hash(box + side) & 255) > 0.93:
                        t.put(box, side, x, y, (200, 40, 40, 255))
    if legs:
        trousers(t, leather, boots=shell, boot_top=8)
        specks("leg", ap.AROUND, 8, 12)
        return
    closed_helm(t, M("plate", "#a8b8c8", "#7088a0", "#30404e"), M("plate", "#f0f4f8", "#d0d8e0", "#808890"), slit_row=3.5)
    tunic(t, leather, belt=BELT, sleeve_len=12)
    t.fill("body", ap.AROUND, shell, 0, 8)
    t.fill("body", ap.AROUND, leather, 9.5, 12)
    specks("body", ap.AROUND, 0, 8)
    pauldrons(t, shell, rows=5, rivet=None)
    bracers(t, shell, 7, 12)
    specks("arm", ap.AROUND, 0, 12)


@armor_set("bear", "Berserkir", (7, 1000))
@pieces(("helmet", "HelmetBerserkerHood", "Headdress of the Bear"), ("chestplate", "ArmorBerserkerChest", "Patterns of the Bear"), ("leggings", "ArmorBerserkerLegs", "Loincloth of the Bear"))
def bear(t, legs):
    fur = M("fur", "#6a4a34", "#4a3222", "#241810", accent="#8a6a50")
    if legs:
        # a fur loincloth over bare legs, wrapped shins
        t.fill("body", ap.AROUND, fur, 9, 12)
        t.band("body", ap.AROUND, 8.5, 9.5, BELT)
        t.fill("leg", ap.AROUND, fur, 0, 5)
        t.fill("leg", ap.AROUND, M("cloth", "#8a7a64", "#6a5c48", "#3a3226"), 8, 12)
        t.fill("leg", "bottom", BOOT)
        for y in range(9, 12):
            t.line("leg", "front", 0, y, 4, y - 0.5, "#3a2414")
        t.shade_sides("leg")
        return
    # a bear's head worn as a hood: its fur all round, its upper jaw and teeth over the brow
    t.fill("head", ("top", "back", "right", "left"), fur)
    t.fill("head", "front", fur, 0, 3)
    for x in (1, 2.5, 5, 6.5):
        t.rect("head", "front", x, 3, x + 0.5, 4, M("plain", "#f0e8d0", "#e0d8c0", "#a09880"), bevel=False)
    t.shade_sides("head")
    # Patterns of the Bear: blue war paint swirling over the bare chest, back and arms, and a necklace of claws
    paint = (40, 90, 170, 255)
    for side in ("front", "back"):
        w, h = t.size("body", side)
        for y in range(h):
            for x in range(w):
                # chevrons down the chest and back, like Valheim's tattoo-like paint
                chevron = (y + abs(x - (w - 1) / 2)) % 6 < 1.2
                if chevron and 5 <= y <= h - 4:
                    t.put("body", side, x, y, paint)
    t.line("body", "front", 1, 1.5, 7, 1.5, "#d8c8a0")
    for x in (1.5, 3, 4.5, 6):
        t.rect("body", "front", x, 1.5, x + 0.5, 2.5, M("plain", "#f0e8d0", "#e8e0c8", "#a09880"), bevel=False)
    for side in ("right", "front", "left", "back"):
        for y in (3, 6, 9):
            t.line("arm", side, 0, y, 4, y + 1, "#2858a8")


@armor_set("vilebone", "Vilebone", (18, 1000))
@pieces(("helmet", "HelmetBerserkerUndead", "Vilebone Visage"), ("chestplate", "ArmorBerserkerUndeadChest", "Vilebone Cage"), ("leggings", "ArmorBerserkerUndeadLegs", "Vilebone Drapes"))
def vilebone(t, legs):
    bone = M("bone", "#e8dcbc", "#c4b48c", "#7a6a48")
    rag = M("cloth", "#5a4e44", "#3e342c", "#1e1814")
    if legs:
        trousers(t, rag, boots=M("cloth", "#4a3e34", "#30281e", "#18140e"), wraps="#c4b48c")
        t.fill("leg", ("front",), bone, 0, 4)
        return
    # a great jaw worn as a visor: bone across the brow and cheeks, fangs down
    t.fill("head", ("top", "back", "right", "left"), rag)
    t.fill("head", ("right", "left"), bone, 2, 7)
    t.fill("head", "front", bone, 0, 2.5)
    for x in range(0, 8, 2):
        t.rect("head", "front", x + 0.5, 2.5, x + 1, 4, M("plain", "#f0e8d0", "#e0d4b4", "#a09070"), bevel=False)
    t.shade_sides("head")
    # a ribcage of bones over the torso
    for side in ("front", "back"):
        for y in (1, 2.5, 4, 5.5, 7):
            t.rect("body", side, 0.5, y, 7.5, y + 0.75, bone, bevel=False)
        t.vband("body", side, 3.5, 4.5, bone, 0.5, 8)
    t.fill("body", ap.AROUND, rag, 8, 12)
    pauldrons(t, bone, rows=4, rivet=None)


@armor_set("ask", "Ask", (28, 1000), repair="netherite")
@pieces(("helmet", "HelmetAshlandsMediumHood", "Hood of Ask"), ("chestplate", "ArmorAshlandsMediumChest", "Breastplate of Ask"), ("leggings", "ArmorAshlandsMediumlegs", "Trousers of Ask"))
def ask(t, legs):
    scales = M("scale", "#7ea04a", "#5a7a30", "#2e4416")
    hide = M("leather", "#8a6448", "#6a4a34", "#38261a")
    if legs:
        trousers(t, hide, boots=BOOT, wraps="#2e4416")
        t.fill("leg", ("right", "left"), scales, 1, 6)
        return
    hood(t, scales, hide, open_rows=(3, 8), open_cols=(1.5, 6.5))
    tunic(t, hide, belt=BELT, sleeve_len=12)
    t.fill("body", ("front", "back"), M("plate", "#b8bcc0", "#8a9094", "#44484c"), 1, 7, 1.5, 6.5)
    t.rivets("body", ("front", "back"), 1.5, "#e8ecf0", every=2, x0=2, x1=6)
    t.fill("body", ("right", "left"), scales, 0, 8)
    pauldrons(t, scales, rows=4, rivet=None)
    bracers(t, hide)


@armor_set("embla", "Embla", (19, 1000))
@pieces(("helmet", "HelmetMage_Ashlands", "Hood of Embla"), ("chestplate", "ArmorMageChest_Ashlands", "Robes of Embla"), ("leggings", "ArmorMageLegs_Ashlands", "Trousers of Embla"))
def embla(t, legs):
    # from Valheim's: a blue hood and a long mottled blue robe flecked pale, sleeves banded red and
    # white, a brown belt; brown boots
    blue = M("mottled", "#6a9ab8", "#3e6a8a", "#1a3a52", accent="#e8eef2")
    if legs:
        trousers(t, blue, boots=M("leather", "#8a5236", "#6a3a24", "#38180c"), boot_top=7.5)
        return
    hood(t, blue, M("cloth", "#2a4a62", "#1a3a52", "#0a1a28"), open_rows=(3, 8), open_cols=(1.5, 6.5))
    tunic(t, blue, belt=BELT, sleeve_len=12)
    for y in (2, 3, 4):
        t.band("arm", ap.AROUND, y, y + 0.5, M("plain", "#e05040" if y % 2 == 0 else "#f0ece4", "#c03a2a" if y % 2 == 0 else "#d8d4cc", "#6a1a10"), bevel=False)
    t.rect("body", "front", 2.5, 4, 5.5, 7.5, M("leather", "#8a6448", "#6a4a34", "#38261a"))


@armor_set("flametal", "Flametal", (38, 1000), sound="netherite", repair="netherite")
@pieces(("helmet", "HelmetFlametal", "Flametal Helmet"), ("chestplate", "ArmorFlametalChest", "Flametal Breastplate"), ("leggings", "ArmorFlametalLegs", "Flametal Greaves"))
def flametal(t, legs):
    steel = DARK_STEEL
    if legs:
        trousers(t, M("leather", "#7a5236", "#5a3a24", "#2e1c10"), boots=M("leather", "#6a4630", "#4a2e1e", "#24160c"), greave=steel, greave_rows=(3, 8))
        t.band("leg", ("front",), 3, 3.5, FLAMETAL, bevel=False)
        return
    closed_helm(t, steel, FLAMETAL, slit_row=3.5)
    t.rivets("head", ap.AROUND, 0.5, "#ffb070", every=2)
    tunic(t, M("chain", "#a0a8b0", "#606870", "#2a3036"), belt=BELT, sleeve_len=8)
    t.fill("body", ("front", "back"), steel, 0.5, 8, 0.5, 7.5)
    t.band("body", ("front", "back"), 3, 3.5, FLAMETAL, bevel=False)
    t.vband("body", "front", 3.75, 4.25, FLAMETAL, 0.5, 8)
    pauldrons(t, steel, rows=5, rivet="#ffb070")
    bracers(t, steel, 7, 12)


@armor_set("protector", "Protector", (44, 1000), sound="netherite", repair="netherite")
@pieces(("helmet", "HelmetDNHeavy", "Helmet of the Protector"), ("chestplate", "ArmorDeepNorthHeavyChest", "Breastplate of the Protector"), ("leggings", "ArmorDeepNorthHeavylegs", "Trousers of the Protector"))
def protector(t, legs):
    # from Valheim's: a steel helm with a gilded face and grey wings (3D), a blue coat banded in gold
    # under a white fur collar, steel-plated sleeves; dark trousers into white fur boots
    steel = M("plate", "#c8d4e0", "#8a98a8", "#3a4656")
    fur = M("fur", "#f4f6f8", "#d8dce2", "#9aa0a8", accent="#ffffff")
    blue = M("cloth", "#3a78a8", "#225888", "#0e2c4a")
    gold = M("plain", "#ffc860", "#f0a030", "#8a5a14")
    if legs:
        trousers(t, M("cloth", "#4a4a50", "#323238", "#18181c"), boots=fur, boot_top=6)
        t.fill("body", ap.AROUND, blue, 9.5, 12)
        return
    t.fill("head", ap.SIDES, steel)
    t.fill("head", "front", GOLD, 1, 8, 1.5, 6.5)
    t.clear("head", "front", 2, 3.5, 6, 4.5)
    t.vband("head", "front", 3.75, 4.25, M("plain", "#8a5a14", "#8a5a14", "#5a3a0a"), 4.5, 8)
    t.shade_sides("head")
    tunic(t, blue, belt=BELT, sleeves=steel, sleeve_len=12)
    for y in (4, 6.5, 10.5):
        t.band("body", ("front", "back"), y, y + 0.5, gold, bevel=False)
    t.vband("body", "front", 3.75, 4.25, gold, 2, 12)
    t.fill("body", ap.AROUND, fur, 0, 2)
    t.band("arm", ap.AROUND, 5, 5.5, gold, bevel=False)
    bracers(t, steel, 8, 12)


@armor_set("vanguard", "Vanguard", (34, 1000), repair="netherite")
@pieces(("helmet", "HelmetDNMediumHood", "Hood of the Vanguard"), ("chestplate", "ArmorDeepNorthMediumChest", "Chestpiece of the Vanguard"), ("leggings", "ArmorDeepNorthMediumlegs", "Trousers of the Vanguard"))
def vanguard(t, legs):
    # from Valheim's: a wide green hood trimmed in white fur, a fur collar, a green and brown banded
    # coat with gold embroidery and fur-cuffed sleeves; green trousers with gold patterns, brown boots
    # with fur cuffs
    green = M("cloth", "#3e7a5a", "#265a40", "#10301e")
    fur = M("fur", "#f0f2f4", "#d0d4d8", "#909498", accent="#ffffff")
    brown = M("leather", "#7a5a40", "#5a3e2a", "#2e1e12")
    gold = "#e0a838"
    if legs:
        trousers(t, green, boots=M("leather", "#7a5236", "#5a3a24", "#2e1c10"), boot_top=8.5)
        t.band("leg", ap.AROUND, 8, 9, fur, bevel=False)
        t.line("leg", "front", 1, 1, 2, 6, gold)
        t.line("leg", "front", 3, 1, 2, 6, gold)
        return
    hood(t, green, fur, open_rows=(3, 8), open_cols=(1.5, 6.5))
    t.band("head", ("right", "left", "back"), 7, 8, fur, bevel=False)
    tunic(t, green, belt=BELT, sleeve_len=12)
    t.fill("body", ap.AROUND, brown, 3, 8)
    for y in (4, 6):
        t.band("body", ap.AROUND, y, y + 0.5, M("plain", gold, gold, "#8a5a14"), bevel=False)
    t.fill("body", ap.AROUND, fur, 0, 2)
    t.band("arm", ap.AROUND, 10.5, 12, fur, bevel=False)
    t.band("arm", ap.AROUND, 6, 6.5, M("plain", gold, gold, "#8a5a14"), bevel=False)


@armor_set("caller", "Caller", (22, 1000))
@pieces(("helmet", "HelmetDNMage", "Headdress of the Caller"), ("chestplate", "ArmorDeepNorthMageChest", "Robes of the Caller"), ("leggings", "ArmorDeepNorthMagelegs", "Trousers of the Caller"))
def caller(t, legs):
    # from Valheim's: a moose skull with its antlers (the 3D part), a purple scarf round the shoulders,
    # a long tan robe edged in orange; brown boots under it
    tan = M("cloth", "#c8b496", "#a8946e", "#6a5a40")
    purple = M("cloth", "#8a5ab8", "#6a3a96", "#3a1c5a")
    orange = M("plain", "#ffb050", "#f08a20", "#904a10")
    bone = M("bone", "#f0e8d8", "#d0c4a8", "#8a7c60")
    if legs:
        trousers(t, tan, boots=M("leather", "#7a5236", "#5a3a24", "#2e1c10"), boot_top=9.5)
        t.vband("leg", "front", 0, 0.5, orange, 0, 9.5)
        return
    t.fill("head", ("top",), bone)
    t.fill("head", "front", bone, 0, 3, 1, 7)
    t.clear("head", "front", 2, 1.5, 3, 2.5)
    t.clear("head", "front", 5, 1.5, 6, 2.5)
    t.fill("head", ("right", "left", "back"), M("fur", "#8a7a6a", "#6a5a4a", "#3a2e24"), 0, 3)
    t.shade_sides("head")
    tunic(t, tan, belt=M("cloth", "#7a4ab0", "#5a2a8a", "#2a0e4a"), sleeve_len=12)
    t.fill("body", ap.AROUND, purple, 0, 3)
    t.fill("arm", ("top",), purple)
    t.fill("arm", ap.AROUND, purple, 0, 2)
    t.line("body", "front", 1, 3, 3.5, 12, "#f08a20")
    t.line("body", "front", 7, 3, 4.5, 12, "#f08a20")
    t.band("arm", ap.AROUND, 11, 12, orange, bevel=False)


@armor_set("fishing", "Fishing", (8, 1000))
@pieces(("helmet", "HelmetFishingHat", "Fishing Hat"),)
def fishing(t, legs):
    hat = M("leather", "#8a7050", "#6a5236", "#3a2c1a")
    t.fill("head", ("top", "back", "right", "left"), hat, 0, 3)
    t.fill("head", "front", hat, 0, 2)
    t.band("head", ap.AROUND, 2, 3, M("cloth", "#a83a2a", "#802a1e", "#401410"), bevel=False)
    t.rect("head", "right", 2, 1, 3, 2, M("plain", "#ff6a4a", "#e04a2a", "#802010"))  # the bobber
    t.shade_sides("head")


@armor_set("crown", "Crown of Valheim", (50, 1000), sound="gold", repair="gold")
@pieces(("helmet", "HelmetCrownofValheim", "Crown of Valheim"),)
def crown(t, legs):
    t.fill("head", ap.AROUND, GOLD, 0.5, 2.5)
    for side in ap.AROUND:
        for x in (0.5, 3.5, 6.5):
            t.rect("head", side, x, 0, x + 1, 0.5, GOLD, bevel=False)
    t.rect("head", "front", 3.25, 1, 4.75, 2.25, M("gem", "#a8e8ff", "#40a8e8", "#1a4a8a"), bevel=False)


@armor_set("dverger", "Dverger Circlet", (2, 1000), sound="gold", repair="gold")
@pieces(("helmet", "HelmetDverger", "Dverger Circlet"),)
def dverger(t, legs):
    band = M("plate", "#c8d0d8", "#909aa4", "#4a525a")
    t.band("head", ap.AROUND, 1.5, 2.5, band)
    t.rect("head", "front", 3, 1, 5, 2.5, M("gem", "#c0fff8", "#40e0d8", "#108a88"), bevel=False)


# Boots' names (Valheim has none): after the set's leggings where they're one outfit
BOOT_NAMES = {
    "leather": "Leather Boots", "troll": "Troll Leather Boots", "bronze": "Bronze Boots", "root": "Root Boots", "iron": "Iron-shod Boots",
    "fenris": "Fenris Boots", "wolf": "Wolf Hide Boots", "lox": "Lox Fur Boots", "padded": "Padded Sabatons", "eitr": "Eitr-weave Boots",
    "carapace": "Carapace Sabatons", "bear": "Wrappings of the Bear", "vilebone": "Vilebone Wrappings", "ask": "Boots of Ask",
    "embla": "Boots of Embla", "flametal": "Flametal Sabatons", "protector": "Boots of the Protector", "vanguard": "Boots of the Vanguard",
    "caller": "Boots of the Caller",
}


# ---- stats -----------------------------------------------------------------------------------
def mc_stats(valheim_armor):
    """Minecraft armor points for the whole set (helmet, chest, legs) and toughness, from Valheim's
    armor per piece: Valheim's late sets are many times its early ones; Minecraft's armor tops out
    at 20, so beyond that it's toughness."""
    total = min(20.0, 3.0 + 6.0 * math.log(1.0 + valheim_armor / 2.0))
    tough = max(0.0, (valheim_armor - 20) / 6.0)
    kb = 0.05 if valheim_armor >= 32 else 0.0
    return total, round(tough, 1), kb


def split(total, slots):
    share = {"helmet": 0.2, "chestplate": 0.4, "leggings": 0.25, "boots": 0.15}
    if len(slots) == 1:
        return {slots[0]: max(1, round(total * share[slots[0]]))}
    return {s: max(1, round(total * share[s])) for s in slots}


# ---- output ----------------------------------------------------------------------------------
BOOT_ROWS = 7.5  # boots are the legs from here down (and their soles)


def paint(s):
    top = ap.ArmorTexture()
    s["paint"](top, False)
    legs = None
    if any(p[0] == "leggings" for p in s["pieces"]):
        legs = ap.ArmorTexture()
        s["paint"](legs, True)
        # Minecraft's boots are drawn in the main layer, over the leggings: the lower legs again
        for side in ap.AROUND + ("bottom",):
            w, h = top.size("leg", side)
            y0 = 0 if side == "bottom" else int(BOOT_ROWS * ap.SCALE)
            for y in range(y0, h):
                for x in range(w):
                    c = legs.get("leg", side, x, y)
                    if c[3]:
                        top.put("leg", side, x, y, c)
    return top, legs


def preview(s, top, legs):
    """The set worn, front and back, from the textures: a flat figure, 6x size."""
    k = 12
    scale = ap.SCALE
    fig = Image.new("RGBA", (2 * 20 * scale * k // 2 + 40, 34 * scale * k // 2), (58, 62, 70, 255))
    def blit(img, box, side, x, y, flip=False):
        u, v, w, h = ap.face_rect(box, side)
        crop = img.crop((u * scale, v * scale, (u + w) * scale, (v + h) * scale))
        if flip:
            crop = crop.transpose(Image.FLIP_LEFT_RIGHT)
        crop = crop.resize((crop.width * k // 2, crop.height * k // 2), Image.NEAREST)
        fig.alpha_composite(crop, (x, y))
    skin = Image.new("RGBA", (64 * scale, 32 * scale), (196, 150, 120, 255))
    def figure(x0, front):
        side = "front" if front else "back"
        t = k * scale // 2  # pixels per texel
        for img in [skin] + [im.img for im in (legs, top) if im]:
            blit(img, "head", side, x0 + 4 * t, 0)
            blit(img, "body", side, x0 + 4 * t, 8 * t)
            blit(img, "arm", side, x0 + 0 * t, 8 * t)
            blit(img, "arm", side, x0 + 12 * t, 8 * t, flip=True)
            if img is not top or True:
                blit(img, "leg", side, x0 + 4 * t, 20 * t)
                blit(img, "leg", side, x0 + 8 * t, 20 * t, flip=True)
    t = k * scale // 2
    figure(10, True)
    figure(10 + 17 * t, False)
    os.makedirs(PREVIEW, exist_ok=True)
    fig.save(os.path.join(PREVIEW, s["id"] + ".png"))


def main():
    only_preview = "--preview" in sys.argv
    rows = []
    for s in SETS:
        top, legs = paint(s)
        preview(s, top, legs)
        if only_preview:
            continue
        top.save(os.path.join(ASSETS, "textures", "entity", "equipment", "humanoid", s["id"] + ".png"))
        layers = {"humanoid": [{"texture": f"valcraft:{s['id']}"}]}
        if legs:
            legs.save(os.path.join(ASSETS, "textures", "entity", "equipment", "humanoid_leggings", s["id"] + ".png"))
            layers["humanoid_leggings"] = [{"texture": f"valcraft:{s['id']}"}]
        path = os.path.join(ASSETS, "equipment", s["id"] + ".json")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        json.dump({"layers": layers}, open(path, "w", encoding="utf-8", newline="\n"), indent=2)
        total, tough, kb = mc_stats(s["armor"])
        if any(p[0] == "leggings" for p in s["pieces"]):
            # Valheim has no boots; Minecraft's set has them (given with the leggings, see ValNet.giveOrTake)
            name = next(p[2] for p in s["pieces"] if p[0] == "leggings")
            s["pieces"] = tuple(s["pieces"]) + (("boots", "-", BOOT_NAMES.get(s["id"], s["hint"] + " Boots")),)
        slots = [p[0] for p in s["pieces"]]
        defense = split(total, slots)
        for slot, prefab, name in s["pieces"]:
            item_id = f"{s['id']}_{slot}"
            rows.append((item_id, prefab, name, slot, s["id"], defense[slot], tough, kb, s["durability"], s["sound"], s["repair"]))
    if only_preview:
        print(f"{len(SETS)} previews in {PREVIEW}")
        return
    out = ["# Generated by tools/armor_sets.py: id, Valheim prefab, slot, equipment asset, defense, toughness, knockback resistance, Valheim durability, sound, repair"]
    for r in rows:
        out.append("\t".join(str(c) for c in (r[0], r[1], r[3], r[4], r[5], r[6], r[7], r[8], r[9], r[10])))
    path = os.path.join(RES, "valcraft", "valheim_armor.tsv")
    open(path, "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
    # item models (the icon is the item texture drawn by tools/draw_items.py) and English names
    lang_path = os.path.join(ASSETS, "lang", "en_us.json")
    lang = json.load(open(lang_path, encoding="utf-8"))
    for item_id, prefab, name, *_ in rows:
        json.dump({"model": {"type": "minecraft:model", "model": f"valcraft:item/{item_id}"}},
                  open(os.path.join(ASSETS, "items", item_id + ".json"), "w", encoding="utf-8", newline="\n"), indent=2)
        json.dump({"parent": "minecraft:item/generated", "textures": {"layer0": f"valcraft:item/{item_id}"}},
                  open(os.path.join(ASSETS, "models", "item", item_id + ".json"), "w", encoding="utf-8", newline="\n"), indent=2)
        lang[f"item.valcraft.{item_id}"] = name
    open(lang_path, "w", encoding="utf-8", newline="\n").write(json.dumps(lang, indent=2, ensure_ascii=False) + "\n")
    # loot lines: the Valheim piece picked up or crafted becomes this one
    loot = "\n".join(f"{prefab} = valcraft:{item_id}" for item_id, prefab, *_ in rows if prefab != "-")
    cs = f"""namespace ValCraft
{{
    // Generated by tools/armor_sets.py: Valheim armor that becomes ValCraft's Minecraft armor (added to
    // ValCraft.loot.txt, see Loot).
    public static class ValheimArmorLoot
    {{
        public const string Marker = "# Valheim armor (ValCraft 0.6.3)";
        public const string Lines = Marker + @"
{loot}
";
    }}
}}
"""
    open(os.path.join(ROOT, "valheim", "ValCraft", "src", "ValheimArmorLoot.cs"), "w", encoding="utf-8", newline="\n").write(cs)
    print(f"{len(SETS)} sets, {len(rows)} pieces: textures, equipment, items, names, loot; previews in {PREVIEW}")
    for s in SETS:
        print(f"  {s['id']:10s} Valheim {s['armor']:>3} -> Minecraft {mc_stats(s['armor'])}")


if __name__ == "__main__":
    main()
