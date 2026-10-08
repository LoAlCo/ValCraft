"""Projects a Valheim armor piece, as sampled worn on the Viking ([Debug] ExportArmor: coloured surface
points and the skeleton), onto Minecraft's armor layout: each body part's box (head, body, arms,
legs) gets its faces from the points around that part, the outermost surface winning, so the
texture is the Valheim armor's own (its chainmail, straps, buckles, trims) laid onto Minecraft's
boxes. Parts standing far off the box (horns, antlers, a long skirt) are left to the 3D parts
(tools/armor_parts.py).

Each part has a frame from the skeleton: the head a cube round the head bone; the body from the
spine to the neck; an arm along shoulder, elbow and wrist; a leg along hip, knee, ankle and toe. The
box is fitted to how far the armor stands out round that part, so a bulky coat fills its faces.

Used by tools/armor_sets.py.
"""
import math
import os

import armorpaint as ap
import armor_points

SAMPLES = os.path.join(os.environ.get("APPDATA", ""), "com.kesomannen.gale", "valheim", "profiles", "MC-V2", "BepInEx", "ValCraft armor")

FRONT = (0.0, 0.0, 1.0)
HEAD = 0.145  # half the size of a helmet round the Viking's head (metres)


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def mul(a, k):
    return (a[0] * k, a[1] * k, a[2] * k)


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def norm(a):
    n = math.sqrt(dot(a, a)) or 1.0
    return (a[0] / n, a[1] / n, a[2] / n)


def seg_dist(p, a, b):
    ab = sub(b, a)
    t = max(0.0, min(1.0, dot(sub(p, a), ab) / max(1e-9, dot(ab, ab))))
    return math.sqrt(dot(sub(p, add(a, mul(ab, t))), sub(p, add(a, mul(ab, t)))))


def pct(values, q):
    if not values:
        return 0.0
    v = sorted(values)
    return v[min(len(v) - 1, int(q * len(v)))]


class Faces:
    """Collects, per texel of each face of one armor box, the outermost points' colours."""

    def __init__(self, box):
        self.box = box
        self.cells = {}  # (side, x, y) -> [best depth, [colours]]

    def put(self, side, u, v, depth, colour):
        """u, v: 0..1 across the face (u from its image-left, v from its top)."""
        w, h = ap.BOXES[self.box][2:5][0], 0
        fw, fh = face_size(self.box, side)
        x = min(fw - 1, max(0, int(u * fw)))
        y = min(fh - 1, max(0, int(v * fh)))
        cell = self.cells.get((side, x, y))
        if cell is None or depth > cell[0] + 0.012:
            self.cells[(side, x, y)] = [depth, [colour]]
        elif depth > cell[0] - 0.012:
            cell[1].append(colour)
            cell[0] = max(cell[0], depth)

    def paint(self, tex, sides=None, fill=2, rows=None):
        """Into the texture: each texel the mean of its outermost colours, then small gaps filled from
        neighbours (fill passes). rows: (y0, y1) in texels to keep (others left as they are)."""
        for side in sides or ap.SIDES:
            fw, fh = face_size(self.box, side)
            grid = {}
            for y in range(fh):
                for x in range(fw):
                    cell = self.cells.get((side, x, y))
                    if cell:
                        cs = cell[1]
                        grid[(x, y)] = tuple(sum(c[i] for c in cs) // len(cs) for i in range(3))
            # gaps between the sample points: from neighbours, a few passes; a face mostly covered gets
            # its last holes filled too (a helmet's open face, mostly empty, stays open)
            covered = len(grid) / max(1, fw * fh)
            passes = fill + (12 if covered > (0.85 if self.box == "head" else 0.6) else 0)
            for n in range(passes):
                more = {}
                need = 2 if n < fill else 1
                for y in range(fh):
                    for x in range(fw):
                        if (x, y) in grid:
                            continue
                        near = [grid[k] for k in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)) if k in grid]
                        if len(near) >= need:
                            more[(x, y)] = tuple(sum(c[i] for c in near) // len(near) for i in range(3))
                if not more:
                    break
                grid.update(more)
            for (x, y), c in grid.items():
                if rows and not (rows[0] * ap.SCALE <= y < rows[1] * ap.SCALE):
                    continue
                tex.put(self.box, side, x, y, punch(c) + (255,))


def face_size(box, side):
    _, _, w, h = ap.face_rect(box, side)
    return w * ap.SCALE, h * ap.SCALE


def punch(c):
    """A touch more contrast and saturation than the soft render, like Minecraft's flat colours."""
    grey = sum(c) / 3
    return tuple(max(0, min(255, int((grey + (v - grey) * 1.12 - 128) * 1.06 + 128))) for v in c)


def box_side(a, b, hw, hd):
    """Which side of a box a point at (a right, b forward) from its axis lies on, and how far out (0..1 across)."""
    if abs(a) / hw > abs(b) / hd:
        return ("right" if a > 0 else "left"), abs(a) / hw
    return ("front" if b > 0 else "back"), abs(b) / hd


def side_u(side, a, b, hw, hd):
    """Across a side face, 0..1 from its image-left (Minecraft's box layout)."""
    if side == "front":
        return (hw - a) / (2 * hw)
    if side == "back":
        return (a + hw) / (2 * hw)
    if side == "right":
        return (b + hd) / (2 * hd)
    return (hd - b) / (2 * hd)


class Limb:
    """A chain of bones (shoulder, elbow, wrist / hip, knee, ankle, toe) and the box rows each spans."""

    def __init__(self, joints, rows, box):
        self.joints, self.rows, self.box = joints, rows, box

    def locate(self, p):
        """(distance from the limb, row 0..12, a, b) for the nearest segment."""
        best = None
        for i in range(len(self.joints) - 1):
            s, e = self.joints[i], self.joints[i + 1]
            d = seg_dist(p, s, e)
            if best is None or d < best[0]:
                axis = norm(sub(e, s))
                fwd = norm(sub(FRONT, mul(axis, dot(FRONT, axis))))
                right = norm(cross(fwd, axis))
                rel = sub(p, s)
                t = dot(rel, sub(e, s)) / max(1e-9, dot(sub(e, s), sub(e, s)))
                row = self.rows[i] + (self.rows[i + 1] - self.rows[i]) * t
                best = (d, row, dot(rel, right), dot(rel, fwd))
        return best


def frames(bones):
    def b(name, fallback=None):
        return bones.get(name, fallback)
    head = add(b("Head"), (0.0, 0.10, 0.03))
    arm = Limb([b("RightArm"), b("RightForeArm"), b("RightHand")], [0.5, 6.0, 11.5], "arm")
    leg = Limb([b("RightUpLeg"), b("RightLeg"), b("RightFoot"), b("RightToeBase")], [0.0, 6.0, 11.0, 12.0], "leg")
    return head, arm, leg


def project(prefab, slot, tex, layer="main"):
    """Paints the sampled Valheim piece into tex. slot: helmet, chestplate, leggings (layer "legs":
    the trousers and waist for the leggings layer)."""
    path = os.path.join(SAMPLES, prefab + ".txt")
    if not os.path.exists(path):
        return False
    bones, pts = armor_points.load(path)
    if not pts or "Head" not in bones:
        return False
    head, arm, leg = frames(bones)
    neck_y, waist_y = bones["Neck"][1], bones["Hips"][1] + 0.08
    spine = bones["Spine1"]

    if slot == "helmet":
        # a head-sized box (the head plus Minecraft's armor round it), centred across on the helmet
        # (the head turns a little in the pose it was sampled in) and with its top at the helmet's top:
        # a cap covers the top of the box, a full helm all of it, an open face stays open
        close = [p for p, n, c in pts if math.sqrt(dot(sub(p, head), sub(p, head))) < 0.3]
        if close:
            cx = (pct([q[0] for q in close], 0.05) + pct([q[0] for q in close], 0.95)) / 2
            cz = (pct([q[2] for q in close], 0.05) + pct([q[2] for q in close], 0.95)) / 2
            top = pct([q[1] for q in close], 0.97)
            head = (cx, min(head[1] + 0.03, top - HEAD), cz)
        hx = hy = hz = HEAD
        near = [(sub(p, head), c) for p, n, c in pts if math.sqrt(dot(sub(p, head), sub(p, head))) < 0.32]
        f = Faces("head")
        for q, c in near:
            a, y, b = q[0] / hx, q[1] / hy, q[2] / hz
            m = max(abs(a), abs(y), abs(b))
            if m > 1.45 or m < 0.55:  # horns and the like stand off; the inside of the helmet doesn't show
                continue
            if abs(y) >= abs(a) and abs(y) >= abs(b):
                side = "top" if y > 0 else "bottom"
                u = (1 - a) / 2
                v = (1 + b) / 2 if side == "top" else (1 - b) / 2
            else:
                side, _ = box_side(a, b, 1.0, 1.0)
                u = side_u(side, a, b, 1.0, 1.0)
                v = (1 - y) / 2
            f.put(side, min(0.999, max(0.0, u)), min(0.999, max(0.0, v)), m, c)
        f.paint(tex, fill=3)
        return True

    if slot == "chestplate":
        body = Faces("body")
        armf = Faces("arm")
        rel = [(p, c) for p, n, c in pts if waist_y - 0.05 < p[1] < neck_y + 0.12 and abs(p[0] - spine[0]) < 0.26]
        hw = max(0.12, pct([abs(p[0] - spine[0]) for p, c in rel], 0.9))
        hd = max(0.07, pct([abs(p[2] - spine[2]) for p, c in rel], 0.9))
        arm_pts = []
        for p, n, c in pts:
            da = arm.locate(p)
            dt = abs(p[0] - spine[0]) - hw
            shoulder_x = bones["RightArm"][0]
            if (p[0] > shoulder_x - 0.02 or da[0] < 0.07) and da[0] < 0.18 and da[1] > -1.0:
                arm_pts.append((da, c))
                continue
            if waist_y - 0.05 < p[1] < neck_y + 0.15:
                a, b = p[0] - spine[0], p[2] - spine[2]
                side, out = box_side(a, b, hw, hd)
                row = (neck_y - p[1]) / (neck_y - waist_y)
                if p[1] > neck_y - 0.02 and out < 0.8:
                    body.put("top", (hw - a) / (2 * hw), (b + hd) / (2 * hd), p[1], c)
                    continue
                body.put(side, min(0.999, max(0.0, side_u(side, a, b, hw, hd))), min(0.999, max(0.0, row)), out, c)
        aw = max(0.04, pct([abs(d[2]) for d, c in arm_pts], 0.9))
        ad = max(0.04, pct([abs(d[3]) for d, c in arm_pts], 0.9))
        for (dist, row, a, b), c in arm_pts:
            if row < 0.5:
                armf.put("top", (aw - a) / (2 * aw), (b + ad) / (2 * ad), -row, c)
                continue
            side, out = box_side(a, b, aw, ad)
            armf.put(side, min(0.999, max(0.0, side_u(side, a, b, aw, ad))), min(0.999, row / 12.0), out, c)
        body.paint(tex, fill=3)
        armf.paint(tex, fill=3)
        return True

    if slot == "leggings":
        legf = Faces("leg")
        waist = Faces("body")
        leg_pts = []
        hip_y = bones["RightUpLeg"][1]
        for p, n, c in pts:
            if p[1] > hip_y - 0.02:
                a, b = p[0] - spine[0], p[2] - bones["Hips"][2]
                waist_pts = (a, b, p[1])
                waist.put(*waist_put(a, b, p[1], waist_y, hip_y), c)
                continue
            if p[0] < -0.02:
                continue  # the other leg (Minecraft mirrors this one)
            leg_pts.append((leg.locate(p), c))
        lw = max(0.05, pct([abs(d[2]) for d, c in leg_pts], 0.9))
        ld = max(0.05, pct([abs(d[3]) for d, c in leg_pts], 0.9))
        for (dist, row, a, b), c in leg_pts:
            if dist > 0.2:
                continue
            if row > 11.8:
                legf.put("bottom", (lw - a) / (2 * lw), (ld - b) / (2 * ld), row, c)
            side, out = box_side(a, b, lw, ld)
            legf.put(side, min(0.999, max(0.0, side_u(side, a, b, lw, ld))), min(0.999, max(0.0, row / 12.0)), out, c)
        legf.paint(tex, fill=3)
        waist.paint(tex, sides=ap.AROUND, fill=3, rows=(8, 12))
        return True
    return False


def waist_put(a, b, y, waist_y, hip_y):
    """The trousers' top round the hips, onto the bottom rows (8-12) of the leggings layer's body box."""
    hw, hd = 0.2, 0.12
    side, out = box_side(a, b, hw, hd)
    row = 8 + (max(0.0, min(1.0, (waist_y + 0.12 - y) / 0.2))) * 4
    return side, min(0.999, max(0.0, side_u(side, a, b, hw, hd))), row / 12.0, out
