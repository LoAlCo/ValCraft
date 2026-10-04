"""ValCraft 3D Weapons: Valheim's weapons as hand-built blocky Minecraft models (tools/blockmodels.py),
each designed part by part from the Valheim model's proportions (measured from [Debug] ExportModels)
and colours. Coordinates: y along the weapon from the grip end (0) to the tip (32), x across, z through
its thickness, both centred on 0.

  python tools/weapons_3d.py [id ...]
"""
import os
import sys

from blockmodels import Mat, Model, write_pack_meta

# ---- materials ------------------------------------------------------------------------------
STEEL = Mat("blade", "eef4fc", "a8b8d0", "6a7890", accent="ffffff")
GOLD = Mat("gold", "ffe080", "e0a030", "8a5a10", accent="fff8d0")
BLUE_GEM = Mat("gem", "b8e0ff", "4a8ae8", "1e3a8a")
DARK_WRAP = Mat("wrap", "8a6a50", "5a4434", "2e2218")
NORD_STEEL = Mat("metal", "a8c4e8", "5a7aa8", "34507a")
NORD_GUARD = Mat("metal", "8a9ab8", "4a5a7a", "2a3048")
DARK_WOOD = Mat("wood", "6a5a56", "4a3e3a", "2a2220")
OAK = Mat("wood", "b08868", "8a6848", "5e4430")
IRON = Mat("metal", "9aa0b0", "5a5e6a", "2e3038")
IRON_EDGE = Mat("plain", "f0f4fc", "c8ccd8", "9aa0b0")
IRON_BLADE = Mat("blade", "c8ccd8", "6a6e7a", "3a3c46", accent="f0f4fc")
SLAYER_BLADE = Mat("blade", "a8c8e8", "3a5a7a", "222838", accent="d8ecff")
SLAYER_CORE = Mat("plain", "a8b4c8", "7a869c", "4a5468")
BLACK = Mat("metal", "5a5a64", "34343c", "18181e")
BLACK_WRAP = Mat("wrap", "5a5a62", "34343a", "1a1a1e")
STRAP = Mat("wrap", "a07050", "6a4830", "3a2618")
BRONZE = Mat("gold", "f8dca0", "c89850", "7a5a28", accent="fff4d8")
BRONZE_BLADE = Mat("blade", "f0d090", "c09050", "7a5a30", accent="fff0c0")


def nord_greatsword():
    m = Model("nord_greatsword")
    # pommel: a gold cage round a blue gem
    m.cbox(4, 0, 1, 4, GOLD).cbox(3, 1, 3.5, 3, BLUE_GEM).cbox(2, 3.5, 4.5, 2, GOLD)
    m.cbox(2, 4.5, 10.5, 2, DARK_WRAP)                                      # wrapped grip
    # guard: a gold block with a gem, arms out to both sides curling up at the tips
    m.cbox(4, 10.5, 13.5, 3, GOLD).cbox(2, 11, 13, 3.4, BLUE_GEM)
    m.box(-6.5, 11, -1, 6.5, 12.5, 1, NORD_GUARD)
    m.box(-7.5, 11, -1, -6, 14.5, 1, GOLD).box(6, 11, -1, 7.5, 14.5, 1, GOLD)
    # blade: long and slim, a gold fuller up its lower part, a stepped point
    m.cbox(4, 13.5, 28, 1.4, STEEL).cbox(1.6, 13.5, 20, 1.8, GOLD)
    m.cbox(2.8, 28, 30, 1.3, STEEL).cbox(1.4, 30, 32, 1.1, STEEL)
    return m


def nord_mace():
    m = Model("nord_mace")
    m.cbox(4, 0, 4, 4, GOLD).cbox(2.4, 1, 3, 4.6, BLUE_GEM)                # gold pommel with a gem
    m.cbox(2, 4, 18, 2, DARK_WOOD)                                          # shaft
    m.cbox(2.6, 8, 9, 2.6, GOLD).cbox(2.6, 14, 15, 2.6, GOLD)               # gold rings
    m.cbox(3.2, 18, 22, 3.2, GOLD)                                          # collar
    # head: a blue block in a gold cage
    m.cbox(9, 22, 30, 5, NORD_STEEL)
    m.cbox(9.6, 22, 23, 5.6, GOLD).cbox(9.6, 29, 30, 5.6, GOLD)
    m.box(-0.8, 23, -2.8, 0.8, 29, 2.8, GOLD).box(-4.8, 23, -0.8, 4.8, 29, 0.8, GOLD)
    m.cbox(2.4, 25, 27, 5.8, BLUE_GEM)
    m.cbox(2, 30, 32, 2, GOLD)                                              # top spike
    return m


def iron_axe():
    m = Model("iron_axe")
    m.cbox(2.4, 0, 1, 2.4, IRON)                                            # butt cap
    m.cbox(2, 1, 29, 2, OAK)                                                # long handle
    m.cbox(3, 22, 28, 3, IRON)                                              # socket
    m.box(-9, 21, -0.7, -1.5, 28, 0.7, IRON_BLADE)                          # head
    m.box(-9, 16.5, -0.7, -6, 21, 0.7, IRON_BLADE)                          # the beard
    m.box(-10, 16.5, -0.45, -9, 28, 0.45, IRON_EDGE)                        # sharp edge
    m.box(1.5, 23, -1, 3.5, 27, 1, IRON)                                    # back of the head
    return m


def slayer():
    m = Model("slayer")
    # ring pommel
    m.box(-2.5, 0, -0.8, 2.5, 1, 0.8, BLACK).box(-2.5, 4, -0.8, 2.5, 5, 0.8, BLACK)
    m.box(-2.5, 1, -0.8, -1.5, 4, 0.8, BLACK).box(1.5, 1, -0.8, 2.5, 4, 0.8, BLACK)
    m.cbox(2, 5, 11, 2, BLACK_WRAP)                                         # grip
    m.cbox(6, 11, 12.5, 3, BLACK).cbox(6.4, 12.5, 13.5, 3.4, STRAP)          # guard, bound in leather
    # a wide dark blade with a bright core, a stepped point
    m.cbox(7, 13.5, 28, 2, SLAYER_BLADE).cbox(2, 14, 27, 2.3, SLAYER_CORE)
    m.cbox(5, 28, 30, 1.9, SLAYER_BLADE).cbox(3, 30, 31.5, 1.7, SLAYER_BLADE).cbox(1, 31.5, 32, 1.5, SLAYER_BLADE)
    return m


def bronze_spear():
    m = Model("bronze_spear")
    m.cbox(2, 0, 1.5, 2, BRONZE)                                            # butt cap
    m.cbox(1.5, 1.5, 23, 1.5, OAK)                                          # long shaft
    m.cbox(2.2, 21, 23.5, 2.2, BRONZE)                                      # collar
    m.cbox(3.4, 23.5, 27, 1, BRONZE_BLADE).cbox(2.2, 27, 30, 0.9, BRONZE_BLADE).cbox(1, 30, 32, 0.8, BRONZE_BLADE)
    return m


# ---- the detailed style: more parts, finer texels (40 units long) ------------------------------
EDGE = Mat("plain", "ffffff", "e8f0fc", "b0bccc")
RIDGE = Mat("metal", "f4f8ff", "c8d4e4", "8a98ac")
GLOW_GEM = Mat("gem", "d8f4ff", "5aa8f8", "1e408e", glow=10)
GOLD_DARK = Mat("gold", "e8b040", "b07818", "6a4208", accent="ffe090")
LEATHER = Mat("wrap", "9a7458", "6a4c36", "3a281c")
RIVET = Mat("plain", "e0e4ec", "a0a8b4", "5a606c")
SLAYER_DARK = Mat("metal", "5a6a8a", "2c3450", "161a28")
SLAYER_EDGE = Mat("plain", "9ab8e0", "6a88b8", "3a507a")
SLAYER_RUNE = Mat("plain", "c8e8ff", "8ac4f0", "4a80c0", glow=7)


def gem_cage(m, y0, w, gem, frame):
    """A gem held in a frame: a base plate, four corner posts, a cap."""
    h = w * 0.75
    m.cbox(w + 1, y0, y0 + 1, w + 1, frame)
    m.cbox(w - 0.6, y0 + 1, y0 + 1 + h, w - 0.6, gem)
    p = w / 2
    for sx in (-1, 1):
        for sz in (-1, 1):
            m.box(sx * p - 0.5, y0 + 1, sz * p - 0.5, sx * p + 0.5, y0 + 1 + h, sz * p + 0.5, frame)
    m.cbox(w, y0 + 1 + h, y0 + 2 + h, w, frame)
    return y0 + 2 + h


def nord_greatsword_detailed():
    # proportions from the Valheim model: the guard a quarter of the way up, a short grip
    m = Model("nord_greatsword", length=40, metres=1.94, grip=6)
    top = gem_cage(m, 0, 2.4, GLOW_GEM, GOLD)                                # pommel (to y 3.8)
    m.cbox(2, top, 9, 2, DARK_WRAP)                                         # wrapped grip
    for y in (5.2, 7.4):
        m.cbox(2.4, y, y + 0.6, 2.4, GOLD)                                  # gold bands on the grip
    # guard: a gold block with a glowing gem each side, steel-blue arms trimmed in gold, the tips
    # curling up towards the blade
    m.cbox(3, 9, 11.4, 2.6, GOLD).cbox(1.5, 9.5, 10.9, 3, GLOW_GEM)
    m.box(-4.6, 9.6, -0.75, 4.6, 10.6, 0.75, NORD_GUARD)
    m.box(-4.6, 10.6, -0.5, 4.6, 11, 0.5, GOLD_DARK)
    for sx in (-1, 1):
        m.box(min(sx * 4.6, sx * 5.6), 9.4, -0.9, max(sx * 4.6, sx * 5.6), 11, 0.9, GOLD)
        m.box(min(sx * 4.8, sx * 5.6), 11, -0.7, max(sx * 4.8, sx * 5.6), 12.2, 0.7, GOLD_DARK)
    # blade: steel, a raised ridge, bright sharp edges, a gold fuller at its base with filigree
    m.cbox(4.4, 11.4, 35, 1.4, STEEL)
    m.cbox(1, 12, 34.5, 1.7, RIDGE)
    m.box(-2.6, 11.4, -0.35, -2.2, 35, 0.35, EDGE).box(2.2, 11.4, -0.35, 2.6, 35, 0.35, EDGE)
    m.cbox(1.8, 11.4, 19, 1.9, GOLD)
    for y in (13, 15.5, 18):
        m.cbox(3, y, y + 0.6, 1.8, GOLD_DARK)                               # filigree across the fuller
    m.cbox(3.4, 35, 37, 1.3, STEEL).cbox(2.2, 37, 38.6, 1.2, STEEL).cbox(1, 38.6, 40, 1, EDGE)
    return m


def nord_mace_detailed():
    m = Model("nord_mace", length=40, metres=1.27, grip=10)
    top = gem_cage(m, 0, 4, GLOW_GEM, GOLD)
    m.cbox(2.4, top, 24, 2.4, DARK_WOOD)                                    # shaft
    m.cbox(2.8, top, 13, 2.8, LEATHER)                                      # leather wrap where it's held
    for y in (13, 17, 21):
        m.cbox(3.1, y, y + 1, 3.1, GOLD)                                    # gold rings
    m.cbox(3.6, 24, 28, 3.6, GOLD).cbox(4.8, 27.4, 28.4, 4.8, GOLD_DARK)    # collar flaring out
    # head: a steel-blue block in a gold cage, gold corner caps, a glowing gem front and back
    m.cbox(11, 28.4, 38, 6, NORD_STEEL)
    m.cbox(11.6, 28.4, 29.4, 6.6, GOLD).cbox(11.6, 37, 38, 6.6, GOLD)
    for x in (-3, 3):
        m.box(x - 0.6, 29.4, -3.3, x + 0.6, 37, 3.3, GOLD)                  # straps over the front and back
    m.box(-5.8, 29.4, -0.6, 5.8, 37, 0.6, GOLD)                             # and round the sides
    for sx in (-1, 1):
        for sy in (28.4, 36.6):
            m.box(sx * 5.9 - 0.8, sy, -3.4, sx * 5.9 + 0.8, sy + 1.4, 3.4, GOLD_DARK)
    m.cbox(2.6, 31.6, 34.6, 6.8, GLOW_GEM)
    m.cbox(2.4, 38, 39.2, 2.4, GOLD).cbox(1.4, 39.2, 40, 1.4, GOLD_DARK)    # top spike
    return m


def iron_axe_detailed():
    m = Model("iron_axe", length=40, metres=0.88, grip=8)
    m.cbox(2.8, 0, 1.4, 2.8, IRON)                                          # butt cap
    m.cbox(2.2, 1.4, 36, 2.2, OAK)                                          # handle
    m.cbox(2.6, 2, 12, 2.6, LEATHER)                                        # leather grip
    m.cbox(3, 12, 12.8, 3, IRON)                                            # iron band
    m.cbox(3.4, 27, 35, 3.4, IRON)                                          # socket
    for y in (28.5, 33):
        m.cbox(1, y, y + 1, 3.8, RIVET)                                     # rivets through it
    # the head: a broad dark blade, its beard reaching down the handle, a bright honed edge
    m.box(-10, 27.5, -0.7, -1.7, 34.5, 0.7, IRON_BLADE)
    m.box(-10, 23.5, -0.7, -7, 27.5, 0.7, IRON_BLADE)
    m.box(-8.5, 21.5, -0.6, -7, 23.5, 0.6, IRON_BLADE)
    m.box(-11, 23.5, -0.45, -10, 34.5, 0.45, IRON_EDGE)
    m.box(-9.5, 21.5, -0.4, -8.5, 23.5, 0.4, IRON_EDGE)
    m.box(-10.6, 34.5, -0.4, -6, 35.3, 0.4, IRON_EDGE)                       # the edge's top horn
    m.box(1.7, 28.5, -1.2, 4, 33.5, 1.2, IRON).box(4, 29.5, -0.9, 4.8, 32.5, 0.9, IRON_EDGE)  # the poll behind
    m.cbox(2.2, 36, 37.5, 2.2, IRON)                                        # cap on top
    return m


def slayer_detailed():
    m = Model("slayer", length=40, metres=1.96, grip=6)
    # a round ring pommel: eight short pieces
    for (x0, y0, x1, y1) in ((-1.5, 0, 1.5, 1), (-1.5, 4, 1.5, 5), (-2.5, 1.5, -1.5, 3.5), (1.5, 1.5, 2.5, 3.5),
                             (-2.2, 0.6, -1.2, 1.6), (1.2, 0.6, 2.2, 1.6), (-2.2, 3.4, -1.2, 4.4), (1.2, 3.4, 2.2, 4.4)):
        m.box(x0, y0, -0.8, x1, y1, 0.8, BLACK)
    m.cbox(1.6, 5, 6, 1.6, BLACK)
    m.cbox(2.2, 6, 11, 2.2, BLACK_WRAP)                                     # grip
    m.cbox(2.6, 7, 7.6, 2.6, STRAP).cbox(2.6, 9.4, 10, 2.6, STRAP)
    # guard: a heavy dark block bound with crossed leather straps
    m.cbox(7, 11, 13, 3.2, BLACK)
    m.cbox(7.6, 13, 14.2, 3.6, STRAP).box(-2.6, 11, -1.8, -1.6, 14.2, 1.8, STRAP).box(1.6, 11, -1.8, 2.6, 14.2, 1.8, STRAP)
    # blade: wide and dark, a pale core with glowing runes, blue-steel edges, an angled point
    m.cbox(7, 14.2, 34, 1.8, SLAYER_DARK)
    m.cbox(1.6, 15, 33, 2.1, SLAYER_CORE)
    for y in (17, 21, 25, 29):
        m.cbox(0.8, y, y + 1.4, 2.3, SLAYER_RUNE)
    m.box(-4, 14.2, -0.5, -3.5, 34, 0.5, SLAYER_EDGE).box(3.5, 14.2, -0.5, 4, 34, 0.5, SLAYER_EDGE)
    m.cbox(5.6, 34, 36, 1.7, SLAYER_DARK).cbox(4, 36, 37.6, 1.6, SLAYER_DARK)
    m.cbox(2.4, 37.6, 39, 1.5, SLAYER_EDGE).cbox(1, 39, 40, 1.4, SLAYER_EDGE)
    return m


def bronze_spear_detailed():
    m = Model("bronze_spear", length=40, metres=2.42, grip=12)
    m.cbox(1.2, 0, 1, 1.2, BRONZE).cbox(2, 1, 2.4, 2, BRONZE)                # butt spike
    m.cbox(1.6, 2.4, 31, 1.6, OAK)                                          # shaft
    m.cbox(2, 9, 16, 2, LEATHER)                                            # leather where it's held
    for y in (9, 15.4):
        m.cbox(2.3, y, y + 0.6, 2.3, BRONZE)
    m.cbox(2.4, 29, 31.4, 2.4, BRONZE).cbox(3, 30.6, 31.4, 3, BRONZE)       # collar
    # a leaf point: widest low down, a raised middle, bright edges
    m.cbox(3, 31.4, 33, 1, BRONZE_BLADE).cbox(4, 33, 35.5, 1, BRONZE_BLADE)
    m.cbox(3, 35.5, 37.5, 0.9, BRONZE_BLADE).cbox(1.8, 37.5, 39, 0.9, BRONZE_BLADE).cbox(0.8, 39, 40, 0.8, BRONZE_BLADE)
    m.cbox(0.8, 31.4, 38, 1.4, BRONZE)                                      # its raised middle
    return m


# ---- tools ----------------------------------------------------------------------------------
STONE = Mat("rough", "c8c8c0", "8e8e88", "5a5a56")
FLINT = Mat("rough", "e8e4dc", "aaa49a", "6e685e")
ANTLER = Mat("rough", "f4ecd8", "d4c4a0", "9a8a68")
BRONZE_HEAD = Mat("metal", "f8dca0", "c89850", "7a5a28")
BRONZE_EDGE = Mat("plain", "fff4d0", "f0d090", "c09050")
BLACKMETAL = Mat("metal", "7ae07a", "246a24", "0e300e")
BLACKMETAL_EDGE = Mat("plain", "b8ffb8", "6ad06a", "2e8a2e")
RED_WOOD = Mat("wood", "a05a4a", "7a3a30", "4a2018")
LASHING = Mat("wrap", "c8a878", "8a6a48", "5a4430")
NORD_EDGE = Mat("plain", "f0f8ff", "c8dcf4", "8aa8d0")
GOLD_TRIM = Mat("gold", "ffe080", "e0a030", "8a5a10", accent="fff8d0")
SCYTHE_STEEL = Mat("blade", "f4f8ff", "c0c8d8", "7a8496", accent="ffffff")
SHOVEL_BLADE = Mat("metal", "6a8aa8", "34506a", "1a2a3a")
SHOVEL_EDGE = Mat("plain", "a8c8e0", "6a8aa8", "34506a")
BRASS = Mat("metal", "f0e0a0", "c0a048", "7a6428")


def leather_grip(m, y0, y1, w=2.6):
    m.cbox(w, y0, y1, w, LEATHER)
    m.cbox(w + 0.4, y1 - 0.6, y1, w + 0.4, IRON)  # a band where the wrap ends


def _x(sx, a, b):
    return min(sx * a, sx * b), max(sx * a, sx * b)


def side(m, sx, a, b, y0, y1, d, mat):
    """A box out to one side (sx = -1 left, 1 right) from a to b across, y0..y1 along, d thick."""
    x0, x1 = _x(sx, a, b)
    return m.box(x0, y0, -d / 2, x1, y1, d / 2, mat)


def pickaxe(name, length_m, head, edge, shaft, socket=IRON):
    """Minecraft's pick shape: a head across the top of the handle, curving down to a point each side."""
    m = Model(name, length=40, metres=length_m, grip=8)
    m.cbox(2.8, 0, 1.4, 2.8, socket)
    m.cbox(2.2, 1.4, 34, 2.2, shaft)
    leather_grip(m, 2, 12)
    m.cbox(4, 31, 37, 3.4, socket)                                          # socket round the head
    for sx in (-1, 1):
        side(m, sx, 2, 8, 32.5, 35.5, 2.4, head)                            # the arm, curving down
        side(m, sx, 8, 11.5, 31.5, 34.5, 2, head)                           # to its point
        side(m, sx, 11.5, 14, 30, 32.5, 1.5, head)
        side(m, sx, 13.2, 14.6, 28.6, 30, 1, edge)
        side(m, sx, 2, 11.5, 35.5, 36.3, 1.6, edge)                         # the lit top edge
    return m


def antler_pickaxe():
    m = Model("antler_pickaxe", length=40, metres=0.98, grip=8)
    m.cbox(2.6, 0, 1.2, 2.6, OAK)
    m.cbox(2.2, 1.2, 33, 2.2, OAK)
    leather_grip(m, 2, 12)
    # an antler lashed across the top: a thick base, two tines curving up, a point behind
    m.box(-9, 30, -1.6, 9, 33.5, 1.6, ANTLER)
    m.cbox(3.6, 29.5, 34, 3.6, LASHING)
    side(m, -1, 6, 9, 33.5, 36, 2.6, ANTLER)
    side(m, -1, 5.5, 8.5, 36, 38.5, 2, ANTLER)
    side(m, -1, 5, 7.5, 38.5, 40, 1.6, ANTLER)
    side(m, -1, 1.5, 4, 33.5, 35.5, 2.2, ANTLER)
    side(m, -1, 1, 3.4, 35.5, 37.5, 1.8, ANTLER)
    side(m, 1, 9, 11.5, 30.5, 32.5, 2.4, ANTLER)
    side(m, 1, 11.5, 13, 29, 31, 1.6, ANTLER)
    return m


def block_axe(name, length_m, stone):
    """Stone / Flint Axe: a rough block of stone lashed across the top of the handle."""
    m = Model(name, length=40, metres=length_m, grip=8)
    m.cbox(2.8, 0, 1.4, 2.8, OAK)
    m.cbox(2.6, 1.4, 38, 2.6, OAK)
    m.box(-6.5, 30, -2.4, 6, 36, 2.4, stone)                                # the stone
    side(m, -1, 6.5, 7.5, 31, 35, 3.6, stone)
    side(m, 1, 6, 7, 31.5, 34.5, 3.2, stone)
    m.box(-4, 36, -1.8, 2, 37, 1.8, stone)                                  # its chipped top
    m.cbox(3.4, 29, 30.4, 3.4, LASHING).cbox(3.4, 35.8, 37, 3.4, LASHING)   # rawhide lashing
    m.box(-1.9, 30.4, -2.7, 1.9, 35.8, 2.7, LASHING)
    return m


def bronze_axe():
    m = Model("bronze_axe", length=40, metres=0.88, grip=8)
    m.cbox(2.6, 0, 1.2, 2.6, BRONZE_HEAD)
    m.cbox(2, 1.2, 38, 2, RED_WOOD)
    leather_grip(m, 2, 12, w=2.4)
    m.cbox(3, 31, 38, 3, BRONZE_HEAD)                                       # socket
    # a fan blade that spreads into two horns, its edge curving in between them
    side(m, -1, 1.5, 7, 32, 37, 1.5, BRONZE_HEAD)
    side(m, -1, 7, 10, 34, 39.5, 1.4, BRONZE_HEAD)
    side(m, -1, 7, 10, 29, 34, 1.4, BRONZE_HEAD)
    side(m, -1, 10, 11, 36, 40, 0.9, BRONZE_EDGE)
    side(m, -1, 10, 11, 27.5, 32, 0.9, BRONZE_EDGE)
    side(m, -1, 10, 10.6, 32, 36, 0.8, BRONZE_EDGE)
    side(m, 1, 1.5, 3.6, 33, 36, 2, BRONZE_HEAD)                            # the back
    return m


def black_metal_axe():
    m = Model("black_metal_axe", length=40, metres=0.93, grip=8)
    m.cbox(2.8, 0, 1.4, 2.8, BLACKMETAL)
    m.cbox(2.2, 1.4, 38, 2.2, RED_WOOD)
    leather_grip(m, 2, 13)
    m.cbox(3.2, 30, 38, 3.2, BLACK)                                         # socket
    # a broad fan of dark green metal, wider towards its edge
    side(m, -1, 1.6, 5, 30.5, 37.5, 1.8, BLACKMETAL)
    side(m, -1, 5, 8.5, 28.5, 39, 1.6, BLACKMETAL)
    side(m, -1, 8.5, 11, 26.5, 40, 1.4, BLACKMETAL)
    side(m, -1, 11, 12, 26.5, 40, 0.9, BLACKMETAL_EDGE)
    side(m, 1, 1.6, 3.4, 32, 36, 2.2, BLACK)
    return m


def jotun_bane():
    m = Model("jotun_bane", length=40, metres=0.83, grip=8)
    m.cbox(3, 0, 1.6, 3, IRON)
    m.cbox(2, 1.6, 34, 2, DARK_WOOD)
    leather_grip(m, 2, 12, w=2.4)
    m.cbox(3.6, 29, 39, 3.6, IRON).cbox(4, 33, 34.2, 4, GOLD_TRIM)
    # a steel head each side, hooked at its ends, trimmed in gold
    for sx in (-1, 1):
        side(m, sx, 1.8, 9, 30, 38, 2, NORD_STEEL)
        side(m, sx, 9, 10.6, 27.5, 40, 1.6, NORD_STEEL)
        side(m, sx, 10.6, 11.4, 27.5, 40, 1, NORD_EDGE)
        side(m, sx, 3, 8.6, 33.6, 34.4, 2.6, GOLD_TRIM)
        side(m, sx, 5.4, 6.2, 30.4, 37.6, 2.6, GOLD_TRIM)
    return m


def nord_axe(name, steel, edge):
    m = Model(name, length=40, metres=0.97, grip=8)
    gem_cage(m, 0, 2.6, GLOW_GEM, GOLD)
    m.cbox(2, 4.6, 37, 2, DARK_WOOD)
    leather_grip(m, 4.6, 13, w=2.4)
    m.cbox(3.2, 29, 38, 3.2, steel).cbox(3.6, 30, 31, 3.6, GOLD_TRIM).cbox(3.6, 36, 37, 3.6, GOLD_TRIM)
    # a big bearded blade, its beard sweeping down the handle, gold along its inner edge
    side(m, -1, 1.6, 6, 30, 37.5, 1.6, steel)
    side(m, -1, 6, 9.5, 25, 38.5, 1.5, steel)
    side(m, -1, 6, 7.5, 22, 25, 1.4, steel)
    side(m, -1, 9.5, 10.5, 24, 39, 0.9, edge)
    side(m, -1, 6.8, 8.4, 21, 22, 0.8, edge)
    side(m, -1, 5.6, 6.4, 25.5, 38, 2.1, GOLD_TRIM)
    side(m, 1, 1.6, 4, 32, 35, 1.8, steel)                                  # the spike behind
    side(m, 1, 4, 6, 32.6, 34.4, 1.2, edge)
    return m


def cultivator():
    m = Model("cultivator", length=40, metres=1.5, grip=10)
    m.cbox(2.4, 0, 1, 2.4, BRASS)
    m.cbox(1.8, 1, 32, 1.8, OAK)
    leather_grip(m, 6, 14, w=2.2)
    # a brass fork: a bridge across the shaft, three tines up
    m.cbox(2.4, 31, 33, 2.4, BRASS).box(-4.4, 33, -1, 4.4, 34.6, 1, BRASS)
    for x in (-3.6, 0, 3.6):
        m.cbox(1.2, 34.6, 39, 1.2, BRASS, x=x).cbox(0.8, 39, 40, 0.8, EDGE, x=x)
    return m


def scythe():
    m = Model("scythe", length=40, metres=1.87, grip=12)
    m.cbox(1.6, 0, 37, 1.6, OAK)                                            # the long snath
    for y in (6, 20):
        side(m, 1, 0.8, 4, y, y + 1.2, 1.2, OAK)                            # its two hand grips
    m.cbox(2.4, 35.5, 38, 2.4, IRON)
    # the long blade out to one side, curving down to its point
    side(m, -1, 1.2, 12, 36, 38, 0.8, SCYTHE_STEEL)
    side(m, -1, 12, 15, 34.5, 37.4, 0.7, SCYTHE_STEEL)
    side(m, -1, 15, 16.4, 32.5, 35.5, 0.6, SCYTHE_STEEL)
    side(m, -1, 1.2, 12, 38, 38.6, 0.5, EDGE)
    return m


def snow_shovel():
    m = Model("snow_shovel", length=40, metres=1.48, grip=6)
    # a D-shaped grip at the bottom, a long dark handle, a broad blade
    m.box(-3, 0, -0.9, 3, 1.4, 0.9, BLACK)
    side(m, -1, 1.8, 3, 1.4, 5, 1.8, BLACK)
    side(m, 1, 1.8, 3, 1.4, 5, 1.8, BLACK)
    m.cbox(1.8, 5, 30, 1.8, BLACK)
    m.cbox(2.6, 28, 31, 2.6, IRON)
    m.box(-6, 30, -0.8, 6, 39, 0.8, SHOVEL_BLADE)
    side(m, -1, 6, 6.6, 30, 39, 2.8, SHOVEL_EDGE)
    side(m, 1, 6, 6.6, 30, 39, 2.8, SHOVEL_EDGE)
    m.box(-6.6, 39, -1.2, 6.6, 40, 1.2, SHOVEL_EDGE)                        # the scraping edge
    m.cbox(0.8, 30.5, 38.5, 1.8, SHOVEL_EDGE)                               # a ridge up the middle
    return m


TOOLS = {
    "antler_pickaxe": antler_pickaxe,
    "bronze_pickaxe": lambda: pickaxe("bronze_pickaxe", 0.93, BRONZE_HEAD, BRONZE_EDGE, RED_WOOD),
    "iron_pickaxe": lambda: pickaxe("iron_pickaxe", 0.89, IRON_BLADE, IRON_EDGE, OAK),
    "black_metal_pickaxe": lambda: pickaxe("black_metal_pickaxe", 0.95, BLACKMETAL, BLACKMETAL_EDGE, RED_WOOD, socket=BLACK),
    "stone_axe": lambda: block_axe("stone_axe", 0.98, STONE),
    "flint_axe": lambda: block_axe("flint_axe", 1.0, FLINT),
    "bronze_axe": bronze_axe,
    "iron_axe": iron_axe_detailed,
    "black_metal_axe": black_metal_axe,
    "jotun_bane": jotun_bane,
    "nord_axe": lambda: nord_axe("nord_axe", NORD_STEEL, NORD_EDGE),
    "thunderblood_axe": lambda: nord_axe("thunderblood_axe", Mat("metal", "ff9a9a", "c03838", "6a1414"), Mat("plain", "ffe0e0", "ffa0a0", "d05050")),
    "frostfire_axe": lambda: nord_axe("frostfire_axe", Mat("metal", "f0fcff", "90d4f4", "4a90c0"), Mat("plain", "ffffff", "e0f8ff", "a8e0f8")),
    "cultivator": cultivator,
    "scythe": scythe,
    "snow_shovel": snow_shovel,
}

WEAPONS = {"nord_greatsword": nord_greatsword_detailed, "nord_mace": nord_mace_detailed,
           "slayer": slayer_detailed, "bronze_spear": bronze_spear_detailed}
SIMPLE = {f.__name__: f for f in (nord_greatsword, nord_mace, iron_axe, slayer, bronze_spear)}
ALL = {**TOOLS, **WEAPONS}


def main():
    import weapons_3d_rest
    write_pack_meta()
    rest = weapons_3d_rest.models()
    every = {**{k: f() for k, f in ALL.items()}, **rest}
    for name in sys.argv[1:] or every:
        boxes, size = every[name].build()
        print(f"{name}: {boxes} boxes, {size}x{size} texture")
    # every weapon in the item table should have one
    tsv = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "valheim_items.tsv"), encoding="utf-8")
    weapons = {c[0] for c in (line.split("\t") for line in tsv) if len(c) > 3 and c[3] == "weapon"}
    missing = sorted(weapons - set(every))
    print(f"{len(every)} 3D models; weapons without one: {', '.join(missing) or 'none'}")


if __name__ == "__main__":
    main()
