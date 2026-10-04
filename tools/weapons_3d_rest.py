"""The rest of ValCraft 3D Weapons: every Valheim weapon not drawn in weapons_3d.py, built by family
(sword, knife, big axe, mace, sledge, spear, atgeir, fist) from the Valheim models' measured
proportions (where the guard or head sits, how wide, how long) and colours. A weapon's forms that
share a model (Nord / Thunderblood / Frostfire, the Bleeding / Storming / Primal ones) share a design
with their own materials. Same coordinates as weapons_3d.py: y from the grip end (0) to the tip (40).
"""
from blockmodels import Mat, Model
from weapons_3d import (BLACK, BLACK_WRAP, BLACKMETAL, BLACKMETAL_EDGE, BRONZE, BRONZE_BLADE, BRONZE_EDGE,
                        BRONZE_HEAD, DARK_WOOD, DARK_WRAP, EDGE, FLINT, GLOW_GEM, GOLD, GOLD_DARK, GOLD_TRIM,
                        IRON, IRON_BLADE, IRON_EDGE, LASHING, LEATHER, NORD_EDGE, NORD_GUARD, NORD_STEEL, OAK,
                        RED_WOOD, RIDGE, RIVET, SLAYER_CORE, SLAYER_DARK, SLAYER_EDGE, SLAYER_RUNE, STEEL,
                        STONE, STRAP, ANTLER, gem_cage, leather_grip, side, slayer_detailed, nord_greatsword_detailed,
                        nord_mace_detailed, bronze_spear_detailed)


def tone(light, mid, dark, style="metal", glow=0):
    return Mat(style, light, mid, dark, glow=glow)


# a material set: blade/head metal, its bright edge, a glowing accent (runes, gems)
class Look:
    def __init__(self, metal, edge, accent=None):
        self.metal, self.edge, self.accent = metal, edge, accent or GLOW_GEM


NORD = Look(NORD_STEEL, NORD_EDGE)
NORD_SILVER = Look(STEEL, EDGE)
THUNDERBLOOD = Look(tone("ff9a9a", "c03838", "6a1414"), Mat("plain", "ffe8e8", "ffb0b0", "d05858"), tone("ffd0d0", "ff6a6a", "a01818", "gem", 10))
FROSTFIRE = Look(tone("f0fcff", "90d4f4", "4a90c0"), Mat("plain", "ffffff", "e8faff", "b0e4f8"), tone("ffffff", "a8f0ff", "4ab0e0", "gem", 10))
DARKSTEEL = Look(tone("8a9ab8", "3a4a6a", "1e2638"), Mat("plain", "a8c0e0", "7a90b8", "4a5a7a"), tone("c8e8ff", "8ac4f0", "4a80c0", "plain", 7))
BLEEDING = Look(tone("e06060", "8a1a1a", "420808"), Mat("plain", "ffb0b0", "e06a6a", "8a2a2a"), tone("ffd0d0", "ff5050", "a01010", "plain", 9))
STORMING = Look(tone("8ae0ff", "2a8ac8", "123a62"), Mat("plain", "e8fcff", "a8ecff", "5ab8e8"), tone("ffffff", "a8f4ff", "4ac0f0", "plain", 11))
PRIMAL = Look(tone("b0f070", "3a9a1a", "143a08"), Mat("plain", "e8ffc8", "b0f080", "5aa828"), tone("f0ffd0", "a8ff60", "4ab018", "plain", 9))
NORD_FORMS = (("nord", NORD), ("thunderblood", THUNDERBLOOD), ("frostfire", FROSTFIRE))
DARK_FORMS = (("", DARKSTEEL), ("blood", BLEEDING), ("storm", STORMING), ("nature", PRIMAL))

WOOD_PALE = Mat("wood", "d8ac80", "b08458", "7a5638")
TAN_WRAP = Mat("wrap", "d0a070", "a07448", "6a4a2c")
RED_WRAP = Mat("wrap", "c04040", "8a2424", "4a1010")
CRYSTAL = tone("fcf8ff", "c8b4f4", "7a68b8", "metal", 4)
TEAL = tone("b0ffe8", "40c8a0", "1a6a50", "metal", 3)
CHITIN = Mat("rough", "fffce0", "e8dc90", "a89c50")
BONE = Mat("rough", "fff8ec", "e4d8c0", "a89c80")
FUR = Mat("rough", "b8a898", "806c5c", "4e4034")
SILVER = tone("ffffff", "d0d8e8", "8a94a8")
FROST_SILVER = tone("f4fcff", "b0dcf4", "6a9ac0")
DYRNWYN_FIRE = tone("ffd080", "f05a20", "8a1a08", "metal", 6)
MIST = tone("e0e8ff", "a8a8f0", "6a5ab8")
CARAPACE = tone("c0dce8", "5a8098", "2a4454")
FEATHER = Mat("rough", "e05a5a", "a02a3a", "5a1420")


# ---- swords ---------------------------------------------------------------------------------
def sword(name, metres, blade, edge, guard, grip, pommel, guard_y=None, guard_w=5.0, blade_w=4.0,
          ends=None, fuller=None, two=False, pommel_style="knob", taper=True):
    """A sword: pommel, grip, guard across, a blade with bright edges and a stepped point.
    guard_y: where the guard sits (one-handed swords about a tenth of the way up, greatswords more)."""
    gy = guard_y if guard_y is not None else (9 if two else 7)
    m = Model(name, length=40, metres=metres, grip=gy * 0.55)
    if pommel_style == "ring":
        for (x0, y0, x1, y1) in ((-1.4, 0, 1.4, 0.9), (-1.4, 3.1, 1.4, 4), (-2.2, 0.9, -1.4, 3.1), (1.4, 0.9, 2.2, 3.1)):
            m.box(x0, y0, -0.7, x1, y1, 0.7, pommel)
        m.cbox(1.4, 4, gy - 4.6, 1.4, pommel)
        p_top = gy - 4.6
    elif pommel_style == "gem":
        p_top = gem_cage(m, 0, 2.2, GLOW_GEM, pommel)
    else:
        m.cbox(2.6, 0, 1.6, 2.6, pommel).cbox(1.8, 1.6, 2.4, 1.8, pommel)
        p_top = 2.4
    m.cbox(2, p_top, gy, 2, grip)
    m.cbox(2.4, gy - 0.7, gy, 2.4, pommel)                                  # ferrule under the guard
    # guard
    m.box(-guard_w / 2, gy, -1, guard_w / 2, gy + 1.4, 1, guard)
    if ends == "ball":
        for sx in (-1, 1):
            side(m, sx, guard_w / 2, guard_w / 2 + 1.2, gy - 0.1, gy + 1.5, 1.6, guard)
    elif ends == "curl":
        for sx in (-1, 1):
            side(m, sx, guard_w / 2 - 0.9, guard_w / 2, gy + 1.4, gy + 2.6, 1.4, guard)
    # blade
    bw = blade_w
    top = 36.5
    m.cbox(bw, gy + 1.4, top, 1.3, blade)
    m.box(-bw / 2 - 0.35, gy + 1.4, -0.35, -bw / 2, top, 0.35, edge).box(bw / 2, gy + 1.4, -0.35, bw / 2 + 0.35, top, 0.35, edge)
    m.cbox(0.8, gy + 2, top - 1, 1.55, RIDGE if fuller is None else fuller)
    m.cbox(bw * 0.7, top, 38.2, 1.2, blade).cbox(bw * 0.4, 38.2, 39.4, 1.1, blade).cbox(0.6, 39.4, 40, 1, edge)
    return m


def nord_sword(form, look):
    names = {"nord": "nord_sword", "thunderblood": "thunderblood_sword", "frostfire": "frostfire_sword"}
    m = sword(names[form], 1.40, look.metal if form != "nord" else STEEL, look.edge if form != "nord" else EDGE,
              GOLD, DARK_WRAP, GOLD, guard_w=6.5, blade_w=3.6, ends="curl", fuller=GOLD, pommel_style="gem")
    m.cbox(1.4, 7.2, 8.6, 2.6, look.accent)                                 # the guard's gem
    return m


def nord_greatsword_form(form, look):
    names = {"thunderblood": "thunderblood_greatsword", "frostfire": "frostfire_greatsword"}
    m = nord_greatsword_detailed()
    m.item_id = names[form]
    swap = {id(STEEL): look.metal, id(EDGE): look.edge, id(GLOW_GEM): look.accent}
    m.boxes = [(lo, hi, swap.get(id(mat), mat)) for lo, hi, mat in m.boxes]
    return m


def slayer_form(name, look):
    m = slayer_detailed()
    m.item_id = name
    swap = {id(SLAYER_DARK): look.metal, id(SLAYER_EDGE): look.edge, id(SLAYER_RUNE): look.accent}
    m.boxes = [(lo, hi, swap.get(id(mat), mat)) for lo, hi, mat in m.boxes]
    return m


def nidhogg(name, look):
    m = sword(name, 1.44, look.metal, look.edge, BLACK, BLACK_WRAP, BLACK, guard_w=9, blade_w=3.8, ends="ball")
    m.cbox(0.7, 10, 30, 1.6, look.accent)                                   # its glowing core
    return m


# ---- knives ---------------------------------------------------------------------------------
def knife(name, metres, blade, edge, grip, guard=None, pommel=None, blade_w=4.4, belly=0, curve=0, grip_top=15,
          guard_w=4.5):
    """A knife: a long grip (a third of it), maybe a small guard, a leaf blade."""
    m = Model(name, length=40, metres=metres, grip=grip_top * 0.5)
    if pommel:
        m.cbox(2.8, 0, 1.6, 2.8, pommel)
    m.cbox(2.4, 1.6 if pommel else 0, grip_top, 2.4, grip)
    if guard:
        m.box(-guard_w / 2, grip_top, -1, guard_w / 2, grip_top + 1.2, 1, guard)
    g = grip_top + (1.2 if guard else 0)
    # the blade, in three widening then narrowing pieces, maybe curving to one side
    m.box(-blade_w / 2, g, -0.6, blade_w / 2 + belly, 28, 0.6, blade)
    m.box(-blade_w / 2 + curve, 28, -0.55, blade_w / 2 + belly + curve, 34, 0.55, blade)
    m.box(-blade_w / 2 + 2 * curve + 0.8, 34, -0.5, blade_w / 2 + 2 * curve - 0.4, 38, 0.5, blade)
    m.box(-0.6 + 2.5 * curve, 38, -0.4, 0.8 + 2.5 * curve, 40, 0.4, edge)
    m.box(-blade_w / 2 - 0.4, g, -0.35, -blade_w / 2, 28, 0.35, edge)       # the sharp edge
    return m


# ---- big axes -------------------------------------------------------------------------------
def big_axe(name, metres, head, edge, shaft, double=False, trim=None, socket=IRON, butt=None, reach=11, top=40):
    """A two-handed axe: a long shaft, a great crescent head at the top (one each side if double)."""
    m = Model(name, length=40, metres=metres, grip=8)
    if butt:
        m.cbox(2.6, 0, 1.4, 2.6, butt)
    m.cbox(2.2, 1.4 if butt else 0, top - 2, 2.2, shaft)
    leather_grip(m, 3, 12)
    m.cbox(3.2, top - 12, top - 2, 3.2, socket)
    for sx in ((-1, 1) if double else (-1,)):
        side(m, sx, 1.6, reach * 0.45, top - 11, top - 3, 1.6, head)
        side(m, sx, reach * 0.45, reach * 0.8, top - 13, top - 1, 1.5, head)
        side(m, sx, reach * 0.8, reach, top - 15, top + 0.0, 1.4, head)
        side(m, sx, reach, reach + 0.9, top - 15, top, 0.9, edge)
        if trim:
            side(m, sx, 1.6, reach * 0.8, top - 7.4, top - 6.6, 1.9, trim)
    if not double:
        side(m, 1, 1.6, 3.6, top - 9, top - 5, 2, socket)                   # the back of the head
    return m


def berserkir(name, look):
    """Berserkir Axes: a pair of dark bearded axes (one shown), red-wrapped."""
    m = Model(name, length=40, metres=0.81, grip=8)
    m.cbox(2.6, 0, 1.4, 2.6, BLACK)
    m.cbox(2.2, 1.4, 38, 2.2, RED_WOOD)
    m.cbox(2.6, 3, 13, 2.6, RED_WRAP)
    m.cbox(3.2, 28, 38, 3.2, BLACK)
    side(m, -1, 1.6, 6, 28, 37.5, 1.6, look.metal)
    side(m, -1, 6, 10, 23, 39, 1.5, look.metal)
    side(m, -1, 10, 11, 23, 39.5, 0.9, look.edge)
    side(m, 1, 1.6, 5, 32, 35, 1.8, look.metal)
    side(m, 1, 5, 7, 32.6, 34.4, 1.2, look.edge)
    m.cbox(0.8, 30, 36, 3.6, look.accent)                                   # runes on the socket
    return m


# ---- maces, clubs, sledges -------------------------------------------------------------------
def club():
    m = Model("club", length=40, metres=1.15, grip=7)
    w = WOOD_PALE
    for i, (y0, y1, s) in enumerate(((0, 8, 2.2), (8, 16, 2.8), (16, 24, 3.6), (24, 32, 4.4), (32, 38, 5), (38, 40, 4))):
        m.cbox(s, y0, y1, s, w)
    m.box(-3.2, 27, -1, -2.2, 29, 1, Mat("wood", "8a6248", "6a4834", "4a3020"))  # a knot
    m.box(2.5, 34, -1.2, 3.4, 36, 1.2, Mat("wood", "8a6248", "6a4834", "4a3020"))
    m.cbox(2.6, 2, 8, 2.6, TAN_WRAP)
    return m


def ball_mace(name, metres, head, shaft, spikes=None, collar=None, pommel=None, r=4.4, grip_y=8, wrap=LEATHER):
    """A round head (built up in layers) on a shaft, studded with spikes."""
    m = Model(name, length=40, metres=metres, grip=grip_y)
    if pommel:
        m.cbox(2.8, 0, 1.6, 2.8, pommel)
    m.cbox(2.2, 1.6 if pommel else 0, 32, 2.2, shaft)
    if wrap:
        m.cbox(2.6, 3, 13, 2.6, wrap)
    if collar:
        m.cbox(3, 27, 30, 3, collar)
    cy = 35
    m.cbox(2 * r, cy - r * 0.6, cy + r * 0.6, 2 * r, head)
    m.cbox(2 * r * 0.75, cy - r, cy + r, 2 * r * 0.75, head)
    m.cbox(2 * r * 0.45, cy - r - 0.6, cy + r + 0.6, 2 * r * 0.45, head)
    if spikes:
        for (x, y, z) in ((r + 1.2, 0, 0), (-r - 1.2, 0, 0), (0, 0, r + 1.2), (0, 0, -r - 1.2), (0, r + 1.4, 0)):
            m.box(x - 0.6, cy + y - 0.6, z - 0.6, x + 0.6, cy + y + 0.6, z + 0.6, spikes)
        for (x, z) in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
            d = r * 0.85
            m.box(x * d - 0.5, cy + 2.2, z * d - 0.5, x * d + 0.5, cy + 3.2, z * d + 0.5, spikes)
            m.box(x * d - 0.5, cy - 3.2, z * d - 0.5, x * d + 0.5, cy - 2.2, z * d + 0.5, spikes)
    return m


def block_mace(name, metres, head, edge, shaft, w=8, d=5, h=8, trim=None, gem=None, pommel=None, wrap=LEATHER,
               flanges=False, top=40, grip_y=8):
    """A hammer-like head: a block across the top, a lit top edge, maybe flanges and a gold frame."""
    m = Model(name, length=40, metres=metres, grip=grip_y)
    if pommel:
        m.cbox(3, 0, 1.8, 3, pommel)
    m.cbox(2.2, 1.8 if pommel else 0, top - h, 2.2, shaft)
    if wrap:
        m.cbox(2.6, 3, 13, 2.6, wrap)
    y0 = top - h
    m.cbox(w, y0, top - 0.8, d, head).cbox(w - 0.6, top - 0.8, top, d - 0.6, edge)
    if flanges:
        for sx in (-1, 1):
            side(m, sx, w / 2, w / 2 + 1.2, y0 + 1, top - 1, d * 0.6, head)
        m.box(-w * 0.3, y0 + 1, d / 2, w * 0.3, top - 1, d / 2 + 1.2, head)
        m.box(-w * 0.3, y0 + 1, -d / 2 - 1.2, w * 0.3, top - 1, -d / 2, head)
    if trim:
        m.cbox(w + 0.5, y0, y0 + 0.8, d + 0.5, trim).cbox(w + 0.5, top - 1.6, top - 0.8, d + 0.5, trim)
        for sx in (-1, 1):
            side(m, sx, w / 2 - 0.6, w / 2 + 0.25, y0 + 0.8, top - 1.6, d + 0.5, trim)
    if gem:
        m.cbox(2.4, y0 + h / 2 - 1.5, y0 + h / 2 + 1.5, d + 0.8, gem)
    return m


def eldner(name, look):
    """Flametal Mace and its forms: a long dark shaft with collars, a spiked ball."""
    m = ball_mace(name, 1.18, look.metal, BLACK, spikes=look.edge, collar=BLACK, pommel=BLACK, r=4.2, wrap=BLACK_WRAP)
    for y in (14, 20, 26):
        m.cbox(2.8, y, y + 0.8, 2.8, BLACK)
    m.cbox(1.2, 33.6, 36.4, 8.6, look.accent)                               # glowing inlay round the ball
    return m


def nord_mace_form(name, look):
    m = nord_mace_detailed()
    m.item_id = name
    swap = {id(NORD_STEEL): look.metal, id(GLOW_GEM): look.accent}
    m.boxes = [(lo, hi, swap.get(id(mat), mat)) for lo, hi, mat in m.boxes]
    return m


def nord_sledge(name, look):
    m = block_mace(name, 1.54, look.metal, look.edge, Mat("wood", "5a6a7a", "3a4654", "222a34"), w=15, d=8, h=12,
                   trim=GOLD_TRIM, gem=look.accent, pommel=GOLD)
    m.cbox(5, 26, 28, 5, Mat("rough", "ffffff", "e8e8e8", "b0b0b0"))          # white fur under the head
    return m


# ---- spears, atgeirs ------------------------------------------------------------------------
def spear(name, metres, point, edge, shaft, collar=None, bands=None, wrap=LEATHER, length=7, width=3.2, barbs=False,
          feathers=None):
    """A long shaft with a leaf point; the hand holds it about a third of the way up."""
    m = Model(name, length=40, metres=metres, grip=12)
    m.cbox(1.4, 0, 1.2, 1.4, collar or IRON)
    p0 = 40 - length
    m.cbox(1.6, 1.2, p0, 1.6, shaft)
    if wrap:
        m.cbox(2, 9, 16, 2, wrap)
    if bands:
        for y in (5, 18, 24):
            m.cbox(2.1, y, y + 0.6, 2.1, bands)
    if collar:
        m.cbox(2.4, p0 - 2, p0, 2.4, collar)
    if feathers:
        for sx in (-1, 1):
            side(m, sx, 1, 2.6, p0 - 4, p0 - 1, 0.5, feathers)
    m.cbox(width * 0.75, p0, p0 + length * 0.25, 0.9, point)
    m.cbox(width, p0 + length * 0.25, p0 + length * 0.55, 0.9, point)
    m.cbox(width * 0.6, p0 + length * 0.55, p0 + length * 0.85, 0.85, point)
    m.cbox(0.8, p0 + length * 0.85, 40, 0.8, edge)
    m.cbox(0.6, p0, p0 + length * 0.8, 1.2, edge)                           # the raised middle
    if barbs:
        for i, y in enumerate((p0 + 1, p0 + 3, p0 + 5)):
            side(m, -1 if i % 2 else 1, width / 2, width / 2 + 1.2, y, y + 1.2, 0.6, edge)
    return m


def splitnir(name, look):
    return spear(name, 2.2, look.metal, look.edge, BLACK, collar=BLACK, bands=SILVER, wrap=BLACK_WRAP, length=9, width=3.4)


def nord_spear(name, look):
    return spear(name, 2.3, look.metal, look.edge, DARK_WOOD, collar=GOLD, bands=GOLD, length=9, width=3.4)


def atgeir(name, metres, blade, edge, shaft, trim=None, hook=True, wrap=LEATHER, length=13, fork=False, socket=IRON):
    """A long shaft, a long curved blade at the top with a hook on its back."""
    m = Model(name, length=40, metres=metres, grip=12)
    m.cbox(1.6, 0, 1.2, 1.6, socket)
    b0 = 40 - length
    m.cbox(1.8, 1.2, b0, 1.8, shaft)
    if wrap:
        m.cbox(2.2, 9, 16, 2.2, wrap)
    m.cbox(2.6, b0 - 2, b0 + 1, 2.6, socket)
    if trim:
        m.cbox(2.9, b0 - 1.2, b0 - 0.4, 2.9, trim)
    if fork:
        # Himminafl: three prongs
        for x in (-3, 0, 3):
            m.cbox(1.2, b0 + 1, 38.5, 1, blade, x=x).cbox(0.8, 38.5, 40, 0.8, edge, x=x)
        m.box(-3.6, b0 + 1, -0.6, 3.6, b0 + 2.4, 0.6, blade)
        return m
    # the long blade, bellying out to one side, curving back to its point
    side(m, -1, -1, 1.6, b0 + 1, 39, 1, blade)
    side(m, -1, 1.6, 3, b0 + 2.5, 36, 0.9, blade)
    side(m, -1, 3, 3.8, b0 + 4, 33, 0.6, edge)
    m.box(-1.2, 39, -0.4, 0.6, 40, 0.4, edge)
    if hook:
        side(m, 1, 1, 2.6, b0 + 2, b0 + 4, 0.9, blade).box(1.8, b0 + 4, -0.4, 2.6, b0 + 5.5, 0.4, edge)
    return m


# ---- fists ----------------------------------------------------------------------------------
def fist(name, metres, glove, cuff, claws=None, studs=None, claw_len=8):
    """A gauntlet held in the hand: a cuff, a block for the fist, claws or studs over the knuckles."""
    m = Model(name, length=40, metres=metres, grip=12)
    m.cbox(6, 0, 8, 5, cuff).cbox(6.6, 7, 8, 5.6, cuff)
    m.cbox(7, 8, 20, 6, glove)
    m.cbox(6.4, 20, 23, 5.4, glove)
    for x in (-2.4, -0.8, 0.8, 2.4):
        m.cbox(1.4, 23, 25, 1.6, glove, x=x, z=0.8)                         # knuckles
        if studs:
            m.cbox(1, 23.4, 24.6, 1, studs, x=x, z=-2.6)
        if claws:
            m.cbox(0.9, 25, 25 + claw_len * 0.6, 0.9, claws, x=x, z=0.8).cbox(0.6, 25 + claw_len * 0.6, 25 + claw_len, 0.6, claws, x=x, z=0.8)
    return m


# ---- every one ------------------------------------------------------------------------------
def models():
    out = {}

    def add(m):
        out[m.item_id] = m
        return m

    # swords
    add(sword("wooden_sword", 0.95, WOOD_PALE, Mat("plain", "e8bc90", "c89a6c", "8a6440"), WOOD_PALE, TAN_WRAP, WOOD_PALE,
              guard_y=9, guard_w=9, blade_w=4.6))
    add(sword("bronze_sword", 0.91, BRONZE_BLADE, BRONZE_EDGE, Mat("metal", "a07850", "6a4c30", "3a2818"), TAN_WRAP, BRONZE,
              guard_y=7.5, guard_w=5.6, blade_w=4.8))
    add(sword("iron_sword", 1.27, IRON_BLADE, IRON_EDGE, BLACK, TAN_WRAP, BLACK, guard_w=5.4, blade_w=3.6))
    add(sword("silver_sword", 1.29, SILVER, EDGE, SILVER, TAN_WRAP, SILVER, guard_w=6.6, blade_w=3.2, ends="curl"))
    add(sword("black_metal_sword", 1.28, BLACKMETAL, BLACKMETAL_EDGE, BLACK, RED_WRAP, BLACK, guard_w=6, blade_w=4.4))
    m = add(sword("mistwalker", 1.41, MIST, EDGE, FROST_SILVER, RED_WRAP, FROST_SILVER, guard_y=8, guard_w=8, blade_w=4.4))
    m.cbox(0.7, 11, 32, 1.6, tone("e8f0ff", "b0b8ff", "6a6ad0", "plain", 6))
    m = add(sword("dyrnwyn", 1.71, DYRNWYN_FIRE, tone("fff0b0", "ffb040", "c05010", "plain", 9), BLACK, BLACK_WRAP, BLACK,
                  guard_w=5, blade_w=3.6))
    for y in (14, 21, 28):
        m.box(-2.3, y, -0.5, -1.6, y + 2, 0.5, tone("fff0b0", "ffb040", "c05010", "plain", 12))  # flames licking the edge
    for suffix, look in DARK_FORMS:
        add(nidhogg({"": "nidhogg", "blood": "nidhogg_the_bleeding", "storm": "nidhogg_the_thundering",
                     "nature": "nidhogg_the_primal"}[suffix], look))
    for form, look in NORD_FORMS:
        add(nord_sword(form, look))
    m = add(sword("krom", 1.91, STEEL, EDGE, BLACK, Mat("wrap", "6a5a4a", "4a3e32", "2a221c"), BLACK, guard_y=9, guard_w=5,
                  blade_w=3.2, two=True))
    m.cbox(2, 8.6, 9.6, 2.6, GOLD).box(-3.6, 9, -0.5, -2.5, 11.4, 0.5, BLACK).box(2.5, 9, -0.5, 3.6, 11.4, 0.5, BLACK)
    for name, look in (("brutal_slayer", BLEEDING), ("scourging_slayer", STORMING), ("primal_slayer", PRIMAL)):
        add(slayer_form(name, look))
    for form, look in NORD_FORMS[1:]:
        add(nord_greatsword_form(form, look))

    # knives
    add(knife("flint_knife", 0.37, FLINT, Mat("plain", "ffffff", "f0ece4", "c0b8ac"), LEATHER, blade_w=5.6, belly=1))
    add(knife("copper_knife", 0.37, tone("f8d8c0", "d0a080", "8a6048"), Mat("plain", "fff0e0", "f0c8a8", "c09070"), LEATHER,
              pommel=tone("f8d8c0", "d0a080", "8a6048"), blade_w=5, belly=0.6))
    m = add(knife("abyssal_razor", 0.49, CHITIN, Mat("plain", "ffffff", "fffce8", "e0d8a8"), Mat("wrap", "8a5040", "4a3a30", "2a2420"),
                  blade_w=5, curve=-0.6))
    for i, y in enumerate((19, 24, 29, 33)):
        side(m, 1 if i % 2 else -1, 2.4, 3.8, y, y + 1.6, 0.7, CHITIN)       # its barbs
    add(knife("silver_knife", 0.64, SILVER, EDGE, TAN_WRAP, guard=SILVER, pommel=SILVER, blade_w=3.2, grip_top=12, guard_w=7))
    add(knife("black_metal_knife", 0.54, BLACKMETAL, BLACKMETAL_EDGE, RED_WRAP, guard=BLACK, blade_w=4.6, belly=1.6,
              curve=1.0, grip_top=14))
    for form, look, name in (("nord", NORD_SILVER, "nord_dagger"), ("thunderblood", THUNDERBLOOD, "thunderblood_dagger"),
                             ("frostfire", FROSTFIRE, "frostfire_dagger")):
        m = add(knife(name, 0.78, look.metal, look.edge, DARK_WRAP, guard=GOLD, pommel=GOLD, blade_w=4, grip_top=11, guard_w=7.5))
        m.cbox(1.2, 12.4, 26, 1.4, GOLD)
        for sx in (-1, 1):
            side(m, sx, 3.75, 4.6, 11, 13, 1.6, GOLD)
    m = add(knife("skoll_and_hati", 0.47, tone("f8c890", "e0a060", "a06a38"), Mat("plain", "fff4e0", "f8d8b0", "d0a070"),
                  Mat("wrap", "3a6a3a", "1e4a1e", "0e2a0e"), guard=GOLD_DARK, blade_w=3.8, grip_top=13))
    side(m, 1, 3, 4.4, 20, 36, 0.8, tone("6ad08a", "1e6a3a", "0a3a1a"))    # Hati, the green twin, alongside
    add(knife("butcher_knife", 0.60, IRON_BLADE, IRON_EDGE, Mat("wood", "8a6040", "604028", "3a2818"), blade_w=5.4, grip_top=13))

    # big axes
    add(big_axe("battleaxe", 1.70, IRON_BLADE, IRON_EDGE, Mat("wood", "8a5a40", "6a4030", "3e2418"), reach=11))
    m = add(big_axe("crystal_battleaxe", 1.77, CRYSTAL, Mat("plain", "ffffff", "f0e8ff", "c8b8f0"), Mat("wood", "8a5a40", "6a4030", "3e2418"),
                    double=True, reach=10, butt=CRYSTAL))
    m.cbox(1.6, 38, 40.4 - 0.4, 1.6, CRYSTAL)
    add(big_axe("black_metal_battleaxe", 1.60, BLACKMETAL, BLACKMETAL_EDGE, RED_WOOD, socket=BLACK, reach=12, butt=BLACKMETAL))
    m = add(big_axe("skull_splittur", 1.78, TEAL, Mat("plain", "e8fff8", "a8f8e0", "5ad0a8"), Mat("wood", "7a6a60", "564a42", "322a24"),
                    reach=11, socket=BLACK))
    side(m, -1, 11.9, 13, 38, 40, 0.7, TEAL)                                 # its hooked top
    for form, look, name in (("nord", NORD, "nord_greataxe"), ("thunderblood", THUNDERBLOOD, "thunderblood_greataxe"),
                             ("frostfire", FROSTFIRE, "frostfire_greataxe")):
        m = add(big_axe(name, 1.82, look.metal, look.edge, DARK_WOOD, double=True, trim=GOLD_TRIM, reach=10, butt=GOLD))
        m.cbox(2, 32, 35, 3.8, look.accent)
    for suffix, look, name in (("", DARKSTEEL, "berserkir_axes"), ("blood", BLEEDING, "bleeding_berserkir_axes"),
                               ("storm", STORMING, "thundering_berserkir_axes"), ("nature", PRIMAL, "primal_berserkir_axes")):
        add(berserkir(name, look))
    m = Model("early_axes", length=40, metres=0.73, grip=8)                  # a bone-bladed pair (one shown)
    m.cbox(2.4, 0, 37, 2.4, Mat("wood", "c89878", "a07458", "6a4a34"))
    m.cbox(2.8, 3, 12, 2.8, LASHING).cbox(3, 28, 36, 3, LASHING)
    side(m, -1, 1.4, 7, 27, 38, 1.4, BONE)
    side(m, -1, 7, 10, 25, 39, 1.3, BONE)
    side(m, -1, 10, 10.8, 25, 39.5, 0.8, Mat("plain", "ffffff", "fffcf4", "e0d8c8"))
    add(m)

    # maces, sledges
    add(club())
    add(ball_mace("bronze_mace", 0.83, BRONZE_HEAD, Mat("wood", "b08868", "8a6848", "5e4430"), spikes=BRONZE_EDGE, collar=BRONZE, pommel=BRONZE, r=4.6))
    add(block_mace("iron_mace", 1.03, IRON, IRON_EDGE, OAK, w=6.4, d=5.4, h=8, flanges=True, pommel=IRON))
    m = add(block_mace("frostner", 0.95, FROST_SILVER, Mat("plain", "ffffff", "e8f8ff", "b0dcf4"), Mat("wood", "8a7a50", "6a5c38", "3e3420"),
                       w=14, d=7, h=9, pommel=FROST_SILVER, wrap=tone("6ab0f0", "2a70c0", "143a6a", "wrap")))
    m.cbox(2.6, 33, 36, 7.6, tone("e8faff", "a8e8ff", "4ab0e0", "gem", 9))   # the frost gem
    m = add(ball_mace("porcupine", 1.24, Mat("metal", "6a6070", "403848", "201c28"), BLACK, spikes=tone("e8fff0", "a8f0c0", "4ab080", "plain", 5),
                      pommel=RED_WRAP, r=4.6, wrap=BLACK_WRAP))
    for y in (18, 24):
        m.cbox(2.6, y, y + 0.8, 2.6, RED_WRAP)
    for name, look in (("thunderblood_mace", THUNDERBLOOD), ("frostfire_mace", FROSTFIRE)):
        add(nord_mace_form(name, look))
    for suffix, look, name in (("", DARKSTEEL, "flametal_mace"), ("blood", BLEEDING, "bloodgeon"),
                               ("storm", STORMING, "storm_star"), ("nature", PRIMAL, "klossen")):
        add(eldner(name, look))
    m = add(block_mace("stagbreaker", 1.36, ANTLER, Mat("plain", "fffcf0", "f4ecd8", "c8b898"), WOOD_PALE, w=14, d=6, h=8, wrap=TAN_WRAP))
    for sx in (-1, 1):                                                      # antler tines sticking out
        side(m, sx, 3, 4.4, 40 - 0.2, 40, 1.2, ANTLER)
        side(m, sx, 7, 8.6, 33, 37, 1.2, ANTLER)
    m = add(block_mace("iron_sledge", 1.36, IRON, IRON_EDGE, RED_WOOD, w=14, d=7, h=9, pommel=IRON, wrap=RED_WRAP))
    m.box(-0.8, 31, -3.9, 0.8, 39.2, 3.9, RED_WRAP).box(-7.2, 34.2, -0.8, 7.2, 35.8, 0.8, RED_WRAP)   # straps bound round it
    m = add(block_mace("demolisher", 1.52, STONE, Mat("plain", "dcdcd4", "b8b8b0", "8a8a84"), RED_WOOD, w=16, d=10, h=14,
                       pommel=BLACK, wrap=RED_WRAP))
    for y in (29, 32, 35):
        m.cbox(1, y, y + 2.2, 10.4, GOLD_TRIM, x=(y - 32) * 1.2)            # the gold tree in the stone
    m.cbox(9, 32.4, 33.2, 10.4, GOLD_TRIM)
    for form, look, name in (("nord", NORD, "nord_sledge"), ("thunderblood", THUNDERBLOOD, "thunderblood_sledge"),
                             ("frostfire", FROSTFIRE, "frostfire_sledge")):
        add(nord_sledge(name, look))

    # spears
    add(spear("flint_spear", 2.2, FLINT, Mat("plain", "ffffff", "f0ece4", "c0b8ac"), WOOD_PALE, collar=LASHING, length=5, width=2.8))
    m = add(spear("ancient_bark_spear", 2.3, tone("d0e8f8", "7a9ab8", "3e5468"), Mat("plain", "f0f8ff", "c8dcf0", "8aa8c8"),
                  Mat("wood", "7a6a5a", "564a3e", "322a22"), collar=BRONZE, length=7, width=2.6))
    for y in (6, 13, 20):
        m.box(-1.3, y, -0.5, -0.8, y + 3, 0.5, Mat("wood", "5a4a3e", "3e342a", "201a14"))  # the bark's twists
    add(spear("fang_spear", 2.2, BONE, Mat("plain", "ffffff", "fffcf4", "e0d8c8"), WOOD_PALE, collar=FUR, length=5, width=2.8))
    add(spear("abyssal_harpoon", 2.0, CHITIN, Mat("plain", "ffffff", "fffce8", "e0d8a8"), Mat("wood", "8a6a60", "5a4a44", "3a3030"),
              collar=LASHING, length=8, width=2.6, barbs=True))
    add(spear("carapace_spear", 2.3, CARAPACE, Mat("plain", "e8f4fc", "b8d4e4", "7a98ac"), Mat("wood", "a07870", "7a5a52", "4a3632"),
              collar=BLACK, length=8, width=3.4, feathers=FEATHER))
    for suffix, look, name in (("", DARKSTEEL, "splitnir"), ("blood", BLEEDING, "splitnir_the_bleeding"),
                               ("storm", STORMING, "splitnir_the_storming"), ("nature", PRIMAL, "splitnir_the_primal")):
        add(splitnir(name, look))
    for form, look, name in (("nord", NORD, "nord_spear"), ("thunderblood", THUNDERBLOOD, "thunderblood_spear"),
                             ("frostfire", FROSTFIRE, "frostfire_spear")):
        add(nord_spear(name, look))

    # atgeirs
    add(atgeir("bronze_atgeir", 2.4, BRONZE_BLADE, BRONZE_EDGE, Mat("wood", "a07a50", "7a5a3a", "4a3624"), socket=BRONZE))
    add(atgeir("iron_atgeir", 2.4, IRON_BLADE, IRON_EDGE, Mat("wood", "8a6a4a", "6a4e34", "3e2c1c")))
    m = add(atgeir("black_metal_atgeir", 2.4, BLACKMETAL, BLACKMETAL_EDGE, RED_WOOD, socket=BLACK, wrap=BLACK_WRAP))
    for y in (28, 29.6, 31.2):
        side(m, 1, 1, 2.6, y, y + 0.8, 1, BLACK)                            # the spines on its back
    m = add(atgeir("himminafl", 2.3, tone("e8f6ff", "8ac4f0", "4a80b8", "metal", 5), tone("ffffff", "c8f0ff", "6ac8f8", "plain", 12),
                   Mat("wood", "a08060", "7a5a40", "4a3424"), wrap=RED_WRAP, fork=True, socket=SILVER, trim=SILVER))
    for form, look, name in (("nord", NORD, "nord_atgeir"), ("thunderblood", THUNDERBLOOD, "thunderblood_atgeir"),
                             ("frostfire", FROSTFIRE, "frostfire_atgeir")):
        add(atgeir(name, 2.4, look.metal, look.edge, DARK_WOOD, trim=GOLD_TRIM, length=15))

    # fists
    add(fist("paws_of_the_bear", 0.6, FUR, LEATHER, claws=BONE))
    add(fist("vilebone_maulclaws", 0.6, Mat("rough", "8a6a5a", "5a4438", "302420"), DARK_WOOD, claws=BONE, claw_len=10))
    add(fist("flesh_rippers", 0.6, FUR, LEATHER, claws=BLACK, claw_len=11))
    for form, look, name in (("nord", NORD, "nord_knucklechains"), ("thunderblood", THUNDERBLOOD, "thunderblood_knucklechains"),
                             ("frostfire", FROSTFIRE, "frostfire_knucklechains")):
        m = add(fist(name, 0.6, GOLD, DARK_WRAP, studs=look.edge))
        m.cbox(7.4, 14, 15, 6.4, look.metal).cbox(7.4, 18, 19, 6.4, look.metal)   # the chains round it
    return out
