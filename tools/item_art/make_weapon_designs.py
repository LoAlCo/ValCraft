"""Writes weapons_custom.txt: Valheim's weapons redrawn at 16x16 from their 3D models (renders from
[Debug] ExportRenders). Each weapon is described part by part as seen on its model: how wide the
blade is and its colours, where a fuller or inlay runs, the guard's length and ends, the grip's wrap,
the pommel, gems. The weapon lies on the diagonal like a Minecraft sword: a cell is (t, k), t the
step along the weapon from the grip end (0) to the tip (15), k across it (-1 the upper-left edge, 0
the middle line, +1 the lower-right side). weapons_custom.txt comes after weapons.txt, so its
drawings win.

  python tools/item_art/make_weapon_designs.py
"""
import os

HERE = os.path.dirname(os.path.abspath(__file__))


class Draw:
    default_shift = 0

    def __init__(self, name, prefab, outline="1a1410", shift=None):
        """shift: moves region() drawings that far down towards the grip end (v), to fit a big head."""
        self.name, self.prefab, self.outline = name, prefab, outline
        self.shift = Draw.default_shift if shift is None else shift
        self.px = {}
        self.colours = {}

    def c(self, key, hexcol):
        self.colours[key] = hexcol
        return self

    def put(self, t, k, key):
        x, y = t, 15 - t + k
        if 0 <= x < 16 and 0 <= y < 16:
            self.px[(x, y)] = key
        return self

    def at(self, x, y, key):
        if 0 <= x < 16 and 0 <= y < 16:
            self.px[(x, y)] = key
        return self

    def run(self, t0, t1, k, key):
        for t in range(t0, t1 + 1):
            self.put(t, k, key)
        return self

    def blade(self, t0, t1, ks, keys, taper=1):
        """keys: one colour per k in ks. The last `taper` steps narrow to the middle line."""
        for t in range(t0, t1 + 1):
            for k, key in zip(ks, keys):
                if t > t1 - taper and k not in (min(ks), min(ks) + 1):
                    continue
                self.put(t, k, key)
        return self

    def guard(self, t, n, key, ends=None, shade=None):
        """A bar across the weapon at t, n cells to each side; ends: a colour for its tips."""
        for j in range(-n, n + 1):
            self.put(t + j, 2 * j, key if not (shade and j > 0) else shade)
        if ends:
            self.put(t - n, -2 * n, ends).put(t + n, 2 * n, ends)
        return self

    def grip(self, t0, t1, keys, k=0):
        for i, t in enumerate(range(t0, t1 + 1)):
            self.put(t, k, keys[i % len(keys)])
        return self

    def region(self, fn):
        """fn(u, v) -> colour or None for every cell: u = x + y across the weapon (15 on the middle
        line, less towards the upper left), v = x - y along it (-15 at the grip end, 15 at the tip)."""
        for y in range(16):
            for x in range(16):
                key = fn(x + y, x - y + self.shift)
                if key:
                    self.px[(x, y)] = key
        return self

    def shaft(self, v0, v1, light="w", dark="W"):
        return self.region(lambda u, v: (light if u == 15 else dark) if v0 <= v <= v1 and u in (15, 16) else None)

    def rows(self):
        return ["".join(self.px.get((x, y), ".") for x in range(16)) for y in range(16)]

    def text(self):
        used = {ch for row in self.rows() for ch in row if ch != "."}
        out = [f"@ {self.name} from={self.prefab} outline={self.outline}"]
        out += [f"{k} {v}" for k, v in self.colours.items() if k in used]
        out += self.rows()
        return "\n".join(out) + "\n"


DESIGNS = []


def design(fn):
    DESIGNS.append(fn)
    return fn


# ---- colour sets for the variants of one model ----------------------------------------------
NORD = {"e": "eef4fc", "b": "a8b8d0", "d": "6a7890"}
NORD_BLOOD = {"e": "ffd0d0", "b": "d84848", "d": "8a1a1a"}
NORD_FROST = {"e": "f4fdff", "b": "a8e4f8", "d": "5aa8d0"}
DARK = {"e": "8a9ab8", "b": "4a4a6a", "d": "2a2a3e"}
DARK_BLOOD = {"e": "ff8a8a", "b": "b02828", "d": "5a1010"}
DARK_STORM = {"e": "a8ecff", "b": "3aa8e0", "d": "1a4a7a"}
DARK_NATURE = {"e": "c8ff88", "b": "58c828", "d": "1e5a10"}


def nord_sword(name, prefab, blade, fuller):
    d = Draw(name, prefab)
    d.c("e", blade["e"]).c("b", blade["b"]).c("d", blade["d"]).c("y", fuller)
    d.c("g", "f0c040").c("G", "a07820").c("k", "2a3a6a").c("h", "4a3a30").c("H", "2e241e")
    d.blade(5, 14, [-1, 0, 1], ["e", "b", "d"]).put(15, -1, "e")
    d.run(5, 11, 0, "y")                       # the gold inlay up the middle
    d.guard(4, 2, "g", ends="G").put(4, 0, "k")  # ornate cross, a dark gem in the middle
    d.grip(1, 3, ["h", "H"])
    d.put(0, 0, "g").put(0, -1, "k")             # gold pommel with its gem
    return d


def nord_greatsword(name, prefab, blade, fuller):
    d = Draw(name, prefab)
    d.c("e", blade["e"]).c("b", blade["b"]).c("y", fuller).c("Y", "a07820")
    d.c("g", "f0c040").c("G", "a07820").c("k", "2a3a6a").c("h", "7a5a40").c("H", "4a3424")
    d.blade(5, 14, [-1, 0], ["e", "b"]).put(15, -1, "e")
    d.run(5, 8, 0, "y").put(5, -1, "y")          # gold fuller at the base of the blade
    d.guard(4, 2, "g").put(4, 0, "k")
    d.put(3, -4, "G").put(7, 4, "G")             # the guard's tips curl up towards the blade
    d.grip(1, 3, ["h", "H"])
    d.put(0, 0, "g").put(0, 1, "G")
    return d


def nidhogg(name, prefab, blade):
    d = Draw(name, prefab, outline="0e0e14")
    d.c("e", blade["e"]).c("b", blade["b"]).c("d", blade["d"]).c("p", "6a4a8a")
    d.c("g", "3a3a44").c("o", "6a6a78").c("h", "2a2a30").c("H", "1a1a1e")
    d.blade(5, 14, [-1, 0, 1], ["e", "b", "d"]).put(15, -1, "e")
    d.run(5, 8, 0, "p")                          # purple near the guard, fading up the blade
    d.guard(4, 3, "g", ends="o")                 # long thin cross with ball ends
    d.grip(1, 3, ["h", "H"])
    d.put(0, 0, "p")
    return d


def slayer(name, prefab, blade):
    d = Draw(name, prefab, outline="0e0e14")
    d.c("e", blade["e"]).c("b", blade["b"]).c("d", blade["d"]).c("w", "e8ecf4")
    d.c("g", "3a3a48").c("h", "2a2a30").c("H", "1a1a1e").c("r", "5a6a80")
    d.blade(5, 14, [-1, 0, 1, 2], ["e", "b", "b", "d"], taper=2).put(15, -1, "e")
    d.run(6, 12, 0, "w")                         # the bright core down the wide blade
    d.guard(4, 1, "g")
    d.grip(2, 3, ["h", "H"])
    d.at(0, 14, "r").at(1, 14, "r").at(0, 15, "r").at(1, 13, "r")  # ring pommel
    return d


@design
def designs_swords():
    out = []
    # Wooden Sword: a thick training sword, flat-tipped, wide wooden cross
    d = Draw("wooden_sword", "SwordWood")
    d.c("e", "d8a478").c("b", "b08458").c("d", "7a5638").c("g", "9a6a44").c("G", "6a4428").c("h", "8a5e3a")
    d.blade(5, 14, [-1, 0, 1], ["e", "b", "d"], taper=0)
    d.guard(4, 2, "g", shade="G").grip(1, 3, ["h"]).put(0, 0, "g")
    out.append(d)
    # Bronze Sword: a broad leaf blade, short dark cross
    d = Draw("bronze_sword", "SwordBronze")
    d.c("e", "e8c4a0").c("b", "c09470").c("d", "8a6448").c("g", "7a5638").c("h", "a07850").c("H", "7a5638")
    d.blade(5, 13, [-1, 0, 1], ["e", "b", "d"]).put(14, -1, "e").put(14, 0, "b").put(15, -1, "e")
    d.put(8, 2, "d").put(9, 2, "d").put(10, 2, "d")  # the leaf's belly
    d.guard(4, 1, "g").grip(1, 3, ["h", "H"]).put(0, 0, "g")
    out.append(d)
    # Iron Sword: straight bright blade, small dark iron cross, tan grip
    d = Draw("iron_sword", "SwordIron")
    d.c("e", "f0f6ff").c("b", "b4c0d0").c("d", "7a8698").c("g", "3a4048").c("h", "c09468").c("H", "8a6444")
    d.blade(5, 14, [-1, 0, 1], ["e", "b", "d"]).put(15, -1, "e")
    d.guard(4, 1, "g").grip(1, 3, ["h", "H"]).put(0, 0, "g")
    out.append(d)
    # Silver Sword: slim pale blade, a curved silver cross bending towards the blade
    d = Draw("silver_sword", "SwordSilver")
    d.c("e", "ffffff").c("b", "c8d0e0").c("g", "dce4f0").c("G", "8a94a8").c("h", "c09468").c("H", "8a6444")
    d.blade(5, 14, [-1, 0], ["e", "b"]).put(15, -1, "e")
    d.guard(4, 2, "g").put(3, -4, "G").put(7, 4, "G")
    d.grip(1, 3, ["h", "H"]).put(0, 0, "g")
    out.append(d)
    # Black Metal Sword: a wide dark green blade, broadest near the tip, red grip
    d = Draw("black_metal_sword", "SwordBlackmetal", outline="050c05")
    d.c("e", "5aa85a").c("b", "1e4a1e").c("d", "0e260e").c("g", "2e6a2e").c("h", "8a2424").c("H", "5a1414")
    d.blade(5, 14, [-1, 0, 1], ["e", "b", "d"]).put(15, -1, "e")
    d.run(9, 13, 2, "d")
    d.guard(4, 1, "g").grip(1, 3, ["h", "H"]).put(0, 0, "g")
    out.append(d)
    # Mistwalker: a pale blue-violet blade, a light blue cross, red grip, gem pommel
    d = Draw("mistwalker", "SwordMistwalker", outline="141428")
    d.c("e", "d8e4ff").c("b", "a4a8f0").c("d", "7a68c8").c("g", "a8d0f0").c("G", "6a90c0").c("h", "a83030").c("H", "6a1818")
    d.blade(5, 14, [-1, 0, 1], ["e", "b", "d"]).put(15, -1, "e")
    d.guard(4, 2, "g", shade="G").grip(1, 3, ["h", "H"]).put(0, 0, "g")
    out.append(d)
    # Dyrnwyn: a burning red-orange blade with dark flecks, a dark blue cross
    d = Draw("dyrnwyn", "SwordDyrnwyn", outline="2a0804")
    d.c("e", "ffb080").c("b", "f04a2a").c("d", "a01a10").c("k", "5a1008").c("g", "2a3a4a").c("h", "6a6a6a").c("H", "4a4a4a").c("p", "5a2a24")
    d.blade(5, 14, [-1, 0, 1], ["e", "b", "d"]).put(15, -1, "e")
    for t in (6, 9, 12):
        d.put(t, 0, "k")
    d.put(8, 1, "e").put(11, -1, "b")
    d.guard(4, 1, "g").grip(1, 3, ["h", "H"]).put(0, 0, "p")
    out.append(d)
    out += [nidhogg("nidhogg", "SwordNiedhogg", {"e": "7ab8e8", "b": "3a5a8a", "d": "2a2a4a"}),
            nidhogg("nidhogg_the_bleeding", "SwordNiedhoggBlood", DARK_BLOOD),
            nidhogg("nidhogg_the_thundering", "SwordNiedhoggLightning", DARK_STORM),
            nidhogg("nidhogg_the_primal", "SwordNiedhoggNature", DARK_NATURE)]
    out += [nord_sword("nord_sword", "SwordGold", NORD, "e0a030"),
            nord_sword("thunderblood_sword", "SwordGold_BloodLightning", NORD_BLOOD, "f0c040"),
            nord_sword("frostfire_sword", "SwordGold_FrostFire", NORD_FROST, "f08a30")]
    # Krom: a very long slim blade, a small spiked dark cross with gold, a long dark grip
    d = Draw("krom", "THSwordKrom")
    d.c("e", "f0f6ff").c("b", "a8b8cc").c("g", "3a3a40").c("y", "d0a040").c("h", "4a4038").c("H", "2e2820")
    d.blade(5, 14, [-1, 0], ["e", "b"]).put(15, -1, "e")
    d.guard(4, 1, "g").put(4, 0, "y").put(3, -3, "g").put(6, 3, "g")
    d.grip(1, 3, ["h", "H"]).put(0, 0, "g")
    out.append(d)
    out += [slayer("slayer", "THSwordSlayer", {"e": "a8c8e8", "b": "3a5a7a", "d": "2a2a40"}),
            slayer("brutal_slayer", "THSwordSlayerBlood", DARK_BLOOD),
            slayer("scourging_slayer", "THSwordSlayerLightning", DARK_STORM),
            slayer("primal_slayer", "THSwordSlayerNature", DARK_NATURE)]
    out += [nord_greatsword("nord_greatsword", "THSwordGold", NORD, "e0a030"),
            nord_greatsword("thunderblood_greatsword", "THSwordGold_BloodLightning", NORD_BLOOD, "f0c040"),
            nord_greatsword("frostfire_greatsword", "THSwordGold_FrostFire", NORD_FROST, "f08a30")]
    return out


def bearded(name, prefab, edge, metal, shade, trim=None, shaft=("9a6a3a", "6a4422")):
    """Nord axe: a big bearded blade on the upper-left side, trimmed in gold, a spike behind."""
    d = Draw(name, prefab, shift=4).c("a", edge).c("m", metal).c("M", shade).c("w", shaft[0]).c("W", shaft[1])
    if trim:
        d.c("g", trim)
    d.shaft(-15, 11)

    def head(u, v):
        low = {14: 7, 13: 7, 12: 5, 11: 5, 10: 3, 9: 3, 8: 2, 7: 2}.get(u)
        if low is not None and low <= v <= 10:
            if u <= 7 + (1 if v in (2, 10) else 0):
                return "a"
            if trim and u == 12:
                return "g"
            return "m" if u <= 11 else "M"
        if u in (17, 18) and 7 <= v <= 8:
            return "M"  # the spike on the back
        return None
    return d.region(head)


def battle(name, prefab, edge, metal, shade, shaft=("9a6a3a", "6a4422"), trim=None, double=False):
    """A big two-handed axe head: a wide crescent on the upper-left side (both sides if double)."""
    d = Draw(name, prefab, shift=4).c("a", edge).c("m", metal).c("M", shade).c("w", shaft[0]).c("W", shaft[1])
    if trim:
        d.c("g", trim)
    d.shaft(-15, 13)

    def head(u, v):
        for side in ((1, -1) if double else (1,)):
            du = (15 - u) if side == 1 else (u - 16)
            if 1 <= du <= 9:
                half = 2 + du // 2
                if abs(v - 8) <= half and not (du >= 7 and abs(v - 8) <= 1):
                    if du >= 8 or abs(v - 8) == half:
                        return "a"
                    if trim and du == 3:
                        return "g"
                    return "m" if du >= 4 else "M"
        return None
    return d.region(head)


def leaf(e, b, d, belly=1, start=-1, tip=14):
    """A knife's leaf blade: edge, middle, back; the back bulges out in the middle."""
    def f(u, v):
        if not (start <= v <= tip):
            return None
        if v == tip:
            return e if u == 15 else None
        if u == 14 and v <= tip - 2:
            return e
        if u == 15:
            return b
        if 16 <= u <= 16 + (belly if 3 <= v <= tip - 5 else 0) and v <= tip - 2:
            return d
        return None
    return f


@design
def designs_knives_axes():
    out = []
    # Flint Knife: a wide pale leaf blade on a long leather grip, no guard
    d = Draw("flint_knife", "KnifeFlint").c("e", "ffffff").c("b", "d8d4cc").c("d", "a09a90").c("h", "b07a58").c("H", "7a5038")
    d.region(leaf("e", "b", "d", belly=2)).shaft(-13, -2, "h", "H")
    out.append(d)
    # Copper Knife: a leaf blade, pinkish copper, a wrapped grip
    d = Draw("copper_knife", "KnifeCopper").c("e", "f8dcc0").c("b", "d0a888").c("d", "9a7458").c("h", "b8805a").c("H", "6a4428")
    d.region(leaf("e", "b", "d", belly=1)).shaft(-13, -2, "h", "H")
    out.append(d)
    # Abyssal Razor: a jagged cream-yellow blade with barbs, a dark striped grip
    d = Draw("abyssal_razor", "KnifeChitin", outline="2a2408").c("e", "fffce0").c("b", "e8dc90").c("d", "b0a050").c("h", "8a5040").c("H", "2e3a30")
    d.region(leaf("e", "b", "d", belly=0))
    d.region(lambda u, v: "d" if (u == 17 and v in (1, 5, 9)) or (u == 18 and v in (2, 6)) else ("b" if u == 13 and v in (3, 8) else None))
    d.grip(1, 6, ["h", "H"])
    out.append(d)
    # Silver Knife: a straight slim blade, a curved cross, silver pommel
    d = Draw("silver_knife", "KnifeSilver").c("e", "ffffff").c("b", "b8c4d8").c("g", "d0d8e8").c("G", "8a94a8").c("h", "b07a58").c("H", "7a5038")
    d.blade(7, 14, [-1, 0], ["e", "b"]).put(15, -1, "e")
    d.guard(6, 2, "g").put(5, -4, "G").put(9, 4, "G")
    d.grip(2, 5, ["h", "H"]).put(1, 0, "g").put(1, -1, "G")
    out.append(d)
    # Black Metal Knife: a curved dark green scimitar blade, red grip
    d = Draw("black_metal_knife", "KnifeBlackMetal", outline="050c05").c("e", "5aa85a").c("b", "1e4a1e").c("d", "0e260e").c("g", "2e6a2e").c("h", "9a2a2a").c("H", "5a1414")
    d.region(leaf("e", "b", "d", belly=2, start=1, tip=15))
    d.guard(6, 1, "g").grip(1, 5, ["h", "H"])
    out.append(d)
    for name, prefab, blade, fuller in (("nord_dagger", "KnifeGold", NORD, "e0a030"),
                                        ("thunderblood_dagger", "KnifeGold_BloodLightning", NORD_BLOOD, "f0c040"),
                                        ("frostfire_dagger", "KnifeGold_FrostFire", NORD_FROST, "f08a30")):
        d = Draw(name, prefab).c("e", blade["e"]).c("b", blade["b"]).c("d", blade["d"]).c("y", fuller)
        d.c("g", "f0c040").c("G", "a07820").c("h", "6a5040").c("H", "4a3424")
        d.blade(7, 14, [-1, 0, 1], ["e", "b", "d"]).put(15, -1, "e").run(7, 11, 0, "y")
        d.guard(6, 2, "g").put(5, -4, "G").put(9, 4, "G")
        d.grip(2, 5, ["h", "H"]).put(1, 0, "g")
        out.append(d)
    # Skoll and Hati: a straight copper knife and a curved green one, side by side
    d = Draw("skoll_and_hati", "KnifeSkollAndHati", outline="0c140c").c("o", "f0b080").c("O", "c08050").c("e", "5ab87a").c("b", "1e5a3a").c("h", "e0a070").c("H", "1e4a2a")
    d.region(lambda u, v: ("o" if u == 12 else "O" if u == 13 else None) if 0 <= v <= 12 else ("H" if u in (12, 13) and -8 <= v <= -1 else None))
    d.region(lambda u, v: ("e" if u == 18 + (1 if 5 <= v <= 10 else 0) else "b" if u == 19 + (1 if 5 <= v <= 10 else 0) else None) if -2 <= v <= 13 else ("h" if u in (18, 19) and -11 <= v <= -3 else None))
    out.append(d)
    # Butcher Knife: a long straight cleaver blade, wooden handle
    d = Draw("butcher_knife", "KnifeButcher").c("e", "f0f4fc").c("b", "b8c4d4").c("d", "8a96a8").c("h", "a07850").c("H", "6a4a30")
    d.region(lambda u, v: None if not (-1 <= v <= 14) else ("e" if u == 13 else "b" if u in (14, 15) else "d" if u == 16 and v < 14 else None))
    d.shaft(-13, -2, "h", "H")
    out.append(d)

    # Stone / Flint Axe: a block of stone lashed across the top of the handle
    for name, prefab, a, m, M in (("stone_axe", "AxeStone", "d8d8d0", "a4a49c", "6e6e68"), ("flint_axe", "AxeFlint", "f0ece4", "c0b8ac", "84786c")):
        d = Draw(name, prefab, shift=3).c("a", a).c("m", m).c("M", M).c("w", "b8884c").c("W", "8a6030")
        d.shaft(-15, 11)
        d.region(lambda u, v: ("a" if v == 9 else "m" if v in (7, 8) else "M") if 5 <= v <= 9 and 9 <= u <= 18 else None)
        out.append(d)
    # Bronze / Iron Axe: a bearded head with two horns, its edge curving in
    for name, prefab, a, m, M in (("bronze_axe", "AxeBronze", "fff0c0", "dcc490", "a88c5a"), ("iron_axe", "AxeIron", "c8ccd8", "4a4a5a", "2a2a36")):
        d = Draw(name, prefab, shift=4).c("a", a).c("m", m).c("M", M).c("w", "8a5a44").c("W", "5a3a2a")
        d.shaft(-15, 13)

        def head(u, v):
            if 13 <= u <= 14 and 6 <= v <= 10:
                return "M"
            if 9 <= u <= 12 and 6 <= v <= 10:
                return "m"
            if 7 <= u <= 8 and (4 <= v <= 6 or 10 <= v <= 12):
                return "a"  # the two horns of the curved edge
            if u == 9 and v in (5, 11):
                return "a"
            return None
        out.append(d.region(head))
    # Black Metal Axe: a wide fan-shaped dark green head
    d = Draw("black_metal_axe", "AxeBlackMetal", outline="050c05", shift=4).c("a", "7ae07a").c("m", "1e5a1e").c("M", "0e300e").c("w", "8a3a3a").c("W", "5a2020")
    d.shaft(-15, 12)
    d.region(lambda u, v: (("a" if u <= 7 else "m" if u <= 11 else "M") if 6 - (14 - u) // 2 <= v <= 10 + (14 - u) // 2 else None) if 6 <= u <= 14 else None)
    out.append(d)
    # Jotun Bane: twin hooked steel heads with gold, one each side of the handle
    d = Draw("jotun_bane", "AxeJotunBane", shift=4).c("a", "e8f0f8").c("m", "8a98b0").c("M", "4a5468").c("g", "f0c040").c("w", "7a5a48").c("W", "4a3628")
    d.shaft(-15, 12)

    def twin(u, v):
        if 6 <= v <= 12 and (9 <= u <= 13 or 18 <= u <= 22):
            if v == 8 and u in (11, 20):
                return "g"
            if u in (13, 18) and v <= 9:
                return "g"
            return "a" if u in (9, 22) or v == 12 else "m" if v >= 9 else "M"
        return None
    out.append(d.region(twin))
    out += [bearded("nord_axe", "AxeGold", "e8f4ff", "4a6a9a", "2e4466", trim="f0c040", shaft=("5a4a48", "3a2e2c")),
            bearded("thunderblood_axe", "AxeGold_BloodLightning", "ffd0d0", "b83030", "6a1414", trim="f0c040", shaft=("5a4a48", "3a2e2c")),
            bearded("frostfire_axe", "AxeGold_FrostFire", "f4fdff", "8ad0f0", "4a90c0", trim="f0c040", shaft=("5a4a48", "3a2e2c")),
            bearded("early_axes", "AxeEarly", "f8f0c8", "d8c890", "a89860", shaft=("c08a6a", "8a5a44"))]
    return out


def ball(c, r, inner, outer, light, spikes=None, spike_key="s"):
    """A round head centred c along the weapon, r pixels across, lit from the upper left."""
    def f(u, v):
        du, dv = u - 15.5, v - c
        dist = (du * du + dv * dv) / 2
        if dist <= r * r:
            return light if du < -r * 0.5 and dv > -r * 0.3 else (inner if du < 0.5 else outer)
        if spikes and dist <= (r + 1.6) ** 2 and (round(du), round(dv)) in spikes:
            return spike_key
        return None
    return f


def block(c, along, across, light, mid, dark, trim=None, trim_key="g"):
    """A hammer head: `along` half-thickness along the weapon, `across` half-width across it."""
    def f(u, v):
        du, dv = u - 15.5, v - c
        if abs(dv) <= along and abs(du) <= across:
            if trim and (abs(dv) == along or abs(du) >= across - 0.5):
                return trim_key
            return light if dv >= along - 1 else (mid if du < 1 else dark)
        return None
    return f


@design
def designs_heavy():
    Draw.default_shift = 4  # big heads: everything sits a little lower on the diagonal
    out = []
    # Battleaxe: one broad iron crescent on a long plain shaft
    out.append(battle("battleaxe", "Battleaxe", "e8eef8", "8a94a4", "4e5664", shaft=("9a7a5a", "6a5038")))
    # Crystal Battleaxe: a double head of violet crystal on a pale shaft
    out.append(battle("crystal_battleaxe", "BattleaxeCrystal", "f8f4ff", "c4b0f4", "8070c0", shaft=("a08a70", "6e5c48"), double=True))
    # Black Metal Battleaxe: one huge dark green crescent, a red band, a dark red shaft
    out.append(battle("black_metal_battleaxe", "BattleaxeBlackmetal", "5ac05a", "184818", "0a2a0a", shaft=("8a4a40", "5a2a24")))
    # Skull Splittur: a hooked teal crescent
    out.append(battle("skull_splittur", "BattleaxeSkullSplittur", "a8fce0", "40c8a0", "1e6a58", shaft=("7a7068", "4e4640")))
    # Nord Greataxe (and its Thunderblood / Frostfire forms): a double head trimmed in gold
    for name, prefab, a, m, M in (("nord_greataxe", "BattleaxeGold", "e0eefc", "5a7aa8", "34507a"),
                                  ("thunderblood_greataxe", "BattleaxeGold_BloodLightning", "ffd0d0", "c03838", "6a1414"),
                                  ("frostfire_greataxe", "BattleaxeGold_FrostFire", "f4fdff", "90d4f4", "4a90c0")):
        out.append(battle(name, prefab, a, m, M, shaft=("5a4a48", "3a2e2c"), trim="f0c040", double=True))
    # Berserkir Axes (four forms): a pair of dark blue axes
    for name, prefab, a, m, M in (("berserkir_axes", "AxeBerzerkr", "a8c0e0", "4a5a7a", "2a3048"),
                                  ("bleeding_berserkir_axes", "AxeBerzerkrBlood", "ff9a9a", "b02a2a", "5a1010"),
                                  ("thundering_berserkir_axes", "AxeBerzerkrLightning", "b0ecff", "3aa0d8", "1a4a7a"),
                                  ("primal_berserkir_axes", "AxeBerzerkrNature", "c8ff90", "50b828", "1e5a10")):
        out.append(bearded(name, prefab, a, m, M, shaft=("8a5a50", "5a3a34")))

    # Club: a thick tapering log with a knot
    d = Draw("club", "Club").c("w", "c09070").c("W", "8a6248").c("k", "6a4834")
    d.region(lambda u, v: None if not (-14 <= v <= 13) else (("w" if u <= 15 else "W") if 15 - (v + 14) // 9 <= u <= 16 + (v + 14) // 9 else None))
    d.put(9, 1, "k").put(11, -1, "k")
    out.append(d)
    # Bronze Mace: a faceted bronze ball
    d = Draw("bronze_mace", "MaceBronze").c("a", "f8d8b0").c("m", "c8a078").c("M", "8a6848").c("w", "b08a68").c("W", "7a5a40")
    d.shaft(-15, 7).region(ball(10, 2.6, "m", "M", "a"))
    out.append(d)
    # Iron Mace: a flanged iron head, an iron collar and butt
    d = Draw("iron_mace", "MaceIron").c("a", "f0f4fc").c("m", "a8b4c4").c("M", "6a7484").c("w", "b08060").c("W", "7a5438")
    d.shaft(-15, 7).region(block(10, 2, 1.5, "a", "m", "M"))
    d.region(lambda u, v: "M" if v in (9, 10, 11) and u in (12, 19) else ("m" if v == -14 and u in (15, 16) else None))
    out.append(d)
    # Frostner: a big frosty hammer head on a blue-wrapped shaft
    d = Draw("frostner", "MaceSilver", outline="0e1e2e").c("a", "f4fcff").c("m", "a8d4f0").c("M", "5a90c0").c("w", "7a6a40").c("W", "4a3e24").c("b", "3a7ab0")
    d.shaft(-15, 5).region(block(9, 2, 3.5, "a", "m", "M"))
    d.region(lambda u, v: "b" if u in (15, 16) and -9 <= v <= -3 else ("a" if v in (-15, -14) and u in (14, 15, 16, 17) else None))
    out.append(d)
    # Porcupine: a dark ball bristling with pale green spikes, a red-banded black shaft
    d = Draw("porcupine", "MaceNeedle", outline="0a140e").c("a", "8a7a9a").c("m", "4a4058").c("M", "2a2434").c("s", "a8f0c0").c("w", "3a3a40").c("W", "1e1e22").c("r", "a02a3a")
    spikes = {(0, 4), (4, 0), (-4, 0), (0, -4), (3, 3), (-3, -3), (3, -3), (-3, 3)}
    d.shaft(-15, 7).region(ball(10, 2.2, "m", "M", "a", spikes=spikes))
    d.region(lambda u, v: "r" if u in (15, 16) and v in (-12, -6, 0) else None)
    out.append(d)
    # Nord Mace (three forms): a blue block head in a gold cage, gold pommel
    for name, prefab, a, m, M in (("nord_mace", "MaceGold", "e0eefc", "5a7aa8", "34507a"),
                                  ("thunderblood_mace", "MaceGold_BloodLightning", "ffd0d0", "c03838", "6a1414"),
                                  ("frostfire_mace", "MaceGold_FrostFire", "f4fdff", "90d4f4", "4a90c0")):
        d = Draw(name, prefab).c("a", a).c("m", m).c("M", M).c("g", "f0c040").c("w", "5a4a48").c("W", "3a2e2c")
        d.shaft(-13, 6).region(block(10, 2.5, 2.5, "a", "m", "M", trim=True))
        d.region(lambda u, v: "g" if v in (-15, -14) and 14 <= u <= 17 else ("g" if u in (15, 16) and v == 5 else None))
        out.append(d)
    # Flametal Mace (four forms): a long dark shaft with collars, a spiked dark ball
    for name, prefab, m, M, s in (("flametal_mace", "MaceEldner", "4a5a7a", "2a3048", "a090b0"),
                                  ("bloodgeon", "MaceEldnerBlood", "a02a2a", "5a1010", "ff9a9a"),
                                  ("storm_star", "MaceEldnerLightning", "3aa0d8", "1a4a7a", "b0ecff"),
                                  ("klossen", "MaceEldnerNature", "50b828", "1e5a10", "c8ff90")):
        d = Draw(name, prefab, outline="0a0a10").c("a", s).c("m", m).c("M", M).c("s", s).c("w", "4a4a5a").c("W", "2a2a34")
        spikes = {(0, 4), (4, 0), (-4, 0), (3, 3), (-3, -3), (3, -3), (-3, 3)}
        d.shaft(-15, 8).region(ball(11, 2.2, "m", "M", "a", spikes=spikes))
        d.region(lambda u, v: "a" if u in (15, 16) and v in (-10, -4) else None)
        out.append(d)
    # Stagbreaker: a head of tangled antlers on a long pale shaft
    d = Draw("stagbreaker", "SledgeStagbreaker").c("a", "f4ecd8").c("m", "c8b898").c("M", "8a7a60").c("w", "d8a880").c("W", "a07858")
    d.shaft(-15, 7)
    d.region(lambda u, v: ("a" if (u + v) % 3 == 0 else "m" if (u - v) % 4 else "M") if 6 <= v <= 12 and 9 <= u <= 22 and not (abs(u - 15.5) > 4.5 and v in (6, 12)) else None)
    out.append(d)
    # Iron Sledge: an iron block bound with a red cross, a red-wrapped handle, iron butt
    d = Draw("iron_sledge", "SledgeIron").c("a", "e8f4fc").c("m", "a0bcd0").c("M", "6a8098").c("r", "a83030").c("w", "8a3a30").c("W", "5a2018")
    d.shaft(-14, 5).region(block(9, 2.5, 3.5, "a", "m", "M"))
    d.region(lambda u, v: "r" if 6 <= v <= 12 and 12 <= u <= 19 and abs((u - 15.5) - (v - 9)) <= 0.6 or 6 <= v <= 12 and 12 <= u <= 19 and abs((u - 15.5) + (v - 9)) <= 0.6 else None)
    d.region(lambda u, v: "a" if v in (-15, -14) and u in (14, 15, 16, 17) else None)
    out.append(d)
    # Demolisher: a huge grey stone block veined with gold, a red-wrapped handle
    d = Draw("demolisher", "SledgeDemolisher").c("a", "c8ccd0").c("m", "6a7078").c("M", "3e4248").c("g", "e0a840").c("w", "a07058").c("W", "6a4838").c("r", "a03030")
    d.shaft(-15, 4).region(block(9, 3.5, 4.5, "a", "m", "M"))
    d.region(lambda u, v: "g" if 6 <= v <= 12 and 12 <= u <= 19 and (u == 15 or (v == 9 and u in (13, 17)) or (v == 11 and u in (14, 18))) else None)
    d.region(lambda u, v: "r" if u in (15, 16) and v in (-9, -6, -3) else None)
    out.append(d)
    # Nord Sledge (three forms): a big blue block in an ornate gold frame, white fur below
    for name, prefab, a, m, M in (("nord_sledge", "SledgeGold", "e0eefc", "5a7aa8", "34507a"),
                                  ("thunderblood_sledge", "SledgeGold_BloodLightning", "ffd0d0", "c03838", "6a1414"),
                                  ("frostfire_sledge", "SledgeGold_FrostFire", "f4fdff", "90d4f4", "4a90c0")):
        d = Draw(name, prefab).c("a", a).c("m", m).c("M", M).c("g", "f0a030").c("f", "f0f0f0").c("w", "4a5a6a").c("W", "2e3a46")
        d.shaft(-15, 4).region(block(9, 3, 4, "a", "m", "M", trim=True))
        d.region(lambda u, v: "f" if v in (4, 5) and 13 <= u <= 18 else ("f" if v in (-15, -14) and u in (14, 15, 16, 17) else None))
        out.append(d)
    Draw.default_shift = 0
    return out


def spear(name, prefab, tip, shaft, collar=None, bands=(), barbs=False, length=8, outline="1a1410", extra=None):
    """A long shaft with a leaf point at the top right. tip/shaft: (light, mid, dark) colours."""
    d = Draw(name, prefab, outline=outline).c("e", tip[0]).c("b", tip[1]).c("d", tip[2]).c("w", shaft[0]).c("W", shaft[1])
    t0 = 15 - length * 2
    d.shaft(-15, t0 - 1)

    def point(u, v):
        if not (t0 <= v <= 15):
            return None
        f = (v - t0) / max(1, 15 - t0)  # 0 at the base of the point .. 1 at its tip
        half = 1.5 if f < 0.35 else (1.0 if f < 0.8 else 0.5)
        du = u - 15.5
        if abs(du) <= half:
            return "e" if du < 0 and (v - t0) % 2 == 0 else ("b" if du < 0.6 else "d")
        if barbs and abs(du) <= half + 1.5 and (v - t0) % 4 == 1:
            return "e"
        return None
    d.region(point)
    if collar:
        d.c("g", collar).region(lambda u, v: "g" if t0 - 2 <= v <= t0 - 1 and 14 <= u <= 17 else None)
    if bands:
        d.c("s", bands[0]).region(lambda u, v: "s" if u in (15, 16) and v in bands[1] else None)
    if extra:
        extra(d)
    return d


def atgeir_blade(name, prefab, edge, metal, shade, shaft, trim=None, hook=True, outline="1a1410"):
    """A long curved blade on a shaft, a hook on its back."""
    d = Draw(name, prefab, outline=outline).c("a", edge).c("m", metal).c("M", shade).c("w", shaft[0]).c("W", shaft[1])
    d.shaft(-15, 2)

    def blade(u, v):
        if 3 <= v <= 15:
            curve = 1 if 6 <= v <= 11 else 0  # the edge bellies out
            if 14 - curve <= u <= 15:
                return "a" if u == 14 - curve else "m"
            if u == 16 and v <= 13:
                return "M"
            if hook and u in (17, 18) and v in (4, 5):
                return "M"
        if trim and v == 2 and 13 <= u <= 18:
            return "g"
        return None
    if trim:
        d.c("g", trim)
    return d.region(blade)


@design
def designs_polearms_tools():
    out = []
    wood = ("c8a070", "8a6844")
    out.append(spear("flint_spear", "SpearFlint", ("f0f0f0", "a8acb0", "6a6e74"), wood, length=3))
    out.append(spear("bronze_spear", "SpearBronze", ("f8d890", "c89848", "8a6428"), ("8a6a50", "5a4434"), collar="d8a040", length=3))
    out.append(spear("ancient_bark_spear", "SpearElderbark", ("c8e0f0", "6a90b0", "3a5068"), ("6a5a50", "3e342c"), length=3,
                     extra=lambda d: d.c("k", "2e2620").region(lambda u, v: "k" if u == 14 and v in (-9, -3, 3) else None)))
    out.append(spear("fang_spear", "SpearWolfFang", ("fffcf0", "e8dcc0", "a89c80"), ("c8a07a", "8a6a4a"), collar="6a5a50", length=3))
    out.append(spear("abyssal_harpoon", "SpearChitin", ("fffce0", "e0d488", "a09448"), ("8a6a60", "3a3a34"), barbs=True, length=4, outline="1e1a08"))
    out.append(spear("carapace_spear", "SpearCarapace", ("b8d0dc", "5a7a8a", "2e4452"), ("a07870", "6a4a44"), collar="a02a3a", length=4))
    for name, prefab, tip in (("splitnir", "SpearSplitner", ("a8b4c4", "4a5464", "262c36")),
                              ("splitnir_the_bleeding", "SpearSplitner_Blood", ("ff9a9a", "b02a2a", "5a1010")),
                              ("splitnir_the_storming", "SpearSplitner_Lightning", ("b0ecff", "3aa0d8", "1a4a7a")),
                              ("splitnir_the_primal", "SpearSplitner_Nature", ("c8ff90", "50b828", "1e5a10"))):
        out.append(spear(name, prefab, tip, ("3a3a42", "1e1e24"), bands=("b8c0cc", (-11, -5, 1, 5)), length=4, outline="0a0a0e"))
    for name, prefab, tip in (("nord_spear", "SpearGold", ("d8ecff", "4a7ab8", "2a4a7a")),
                              ("thunderblood_spear", "SpearGold_BloodLightning", ("ffd0d0", "c03838", "6a1414")),
                              ("frostfire_spear", "SpearGold_FrostFire", ("f4fdff", "90d4f4", "4a90c0"))):
        out.append(spear(name, prefab, tip, ("5a4a48", "3a2e2c"), collar="f0b040", bands=("f0b040", (-9,)), length=5))

    out.append(atgeir_blade("bronze_atgeir", "AtgeirBronze", "f8e0a0", "d0a850", "8a6a30", ("a07a50", "6a5034")))
    out.append(atgeir_blade("iron_atgeir", "AtgeirIron", "e0e8f4", "6a7a90", "3a4454", ("8a6a4a", "5a4430")))
    out.append(atgeir_blade("black_metal_atgeir", "AtgeirBlackmetal", "5ac05a", "184818", "0a2a0a", ("8a3a34", "5a2020"), outline="050c05"))
    for name, prefab, a, m, M in (("nord_atgeir", "AtgeirGold", "e0eefc", "5a7aa8", "34507a"),
                                  ("thunderblood_atgeir", "AtgeirGold_BloodLightning", "ffd0d0", "c03838", "6a1414"),
                                  ("frostfire_atgeir", "AtgeirGold_FrostFire", "f4fdff", "90d4f4", "4a90c0")):
        out.append(atgeir_blade(name, prefab, a, m, M, ("5a4a48", "3a2e2c"), trim="f0b040"))
    # Himminafl: a three-pronged pale blue fork, a red-wrapped shaft
    d = Draw("himminafl", "AtgeirHimminAfl", outline="0e1a2a").c("a", "e8f6ff").c("m", "8ac4f0").c("M", "4a80b8").c("r", "a8303a").c("w", "a08060").c("W", "6a5038")
    d.shaft(-15, 4).region(lambda u, v: "r" if u in (15, 16) and v in (-6, -4, -2, 0) else None)
    d.region(lambda u, v: ("a" if v >= 13 else "m") if (u in (10, 11, 15, 16, 20, 21) and 6 <= v <= 14) else ("M" if v in (4, 5) and 10 <= u <= 21 else None))
    out.append(d)

    # Antler Pickaxe: a forked antler lashed across the handle
    d = Draw("antler_pickaxe", "PickaxeAntler").c("a", "fff8e8").c("m", "e0d0b0").c("M", "a08c68").c("r", "c07060").c("w", "d8a878").c("W", "a07850")
    d.shaft(-15, 7)
    d.region(lambda u, v: ("m" if abs(u - 15.5) < 5 else "a") if 7 <= v <= 9 and 8 <= u <= 23 else (("a" if v >= 12 else "m") if u in (9, 12) and 10 <= v <= 13 else ("r" if v == 8 and u in (15, 16) else None)))
    out.append(d)
    # Snow Shovel: a dark blue glassy blade, a dark bent handle
    d = Draw("snow_shovel", "Shovel", outline="0a1018").c("a", "8ab0c8").c("m", "3a5a74").c("M", "1e3448").c("w", "3a3a40").c("W", "1e1e22").c("o", "f08a30")
    d.shaft(-12, 5).region(block(9, 3, 3, "a", "m", "M"))
    d.region(lambda u, v: "w" if v in (-14, -13) and 13 <= u <= 18 else ("o" if v == -15 and u == 18 else None))
    out.append(d)
    # Cultivator: a gold three-tined fork on a long shaft
    d = Draw("cultivator", "Cultivator").c("a", "fff0b0").c("m", "c8a848").c("M", "8a7028").c("w", "a07858").c("W", "6a5038")
    d.shaft(-15, 4)
    d.region(lambda u, v: ("a" if v >= 12 else "m") if (u in (10, 11, 15, 16, 20, 21) and 6 <= v <= 13) else ("M" if v in (4, 5) and 10 <= u <= 21 else None))
    out.append(d)
    return out


def main():
    out = ["# Generated by make_weapon_designs.py: weapons drawn from their 3D models. Edit there, or hand-edit", "# here and stop generating that one.", ""]
    n = 0
    for fn in DESIGNS:
        for d in fn():
            out.append(d.text())
            n += 1
    open(os.path.join(HERE, "weapons_custom.txt"), "w", encoding="utf-8").write("\n".join(out))
    print(f"wrote {n} weapon drawings")


if __name__ == "__main__":
    main()
