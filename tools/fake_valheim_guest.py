"""A stand-in Valheim for a second, local Minecraft client, to try ValCraft multiplayer on one PC.

The guest client (tools/run_guest.ps1: -Dvalcraft.link=Local\\ValCraft_guest, --username Guest) is
"played" by this script, the way a friend's Valheim would play theirs:
  * it says what a friend's Valheim says once the friends have agreed who hosts: join the host's
    shared world (ValState.mpMode = 2, mpLink = the link the host's Minecraft reports);
  * it copies the ground the host's real Valheim describes (read without consuming it) to the guest,
    so the guest stands on the real terrain near the host, and the guest's client forwards it on;
  * it starts the guest beside the host, then follows the host around;
  * a few seconds in, it "picks up" Valheim loot (IN_GIVE), which a guest's server must hand over;
  * it prints the events the guest's Minecraft sends back (hits, meads, ...).

    python tools/fake_valheim_guest.py [seconds] [slot_angle_degrees] [slot_radius]
"""
import ctypes
import math
import mmap
import os
import struct
import sys
import time

MAGIC, VERSION = 0x434C4156, 10
NAME = os.environ.get("VALCRAFT_LINK", "Local\\ValCraft_guest")
HOST = "Local\\ValCraft_v1"
OFF_VAL, OFF_MC, OFF_OVL, OFF_OVL_HDR = 0x100, 0x200, 0x300, 0x340
OFF_IN, OFF_ACTORS, OFF_EVENTS = 0x1000, 0x12000, 0x17000
OFF_COL = 0x20000
COL_BYTES = 32 << 20
COL_DATA = COL_BYTES - 0x80
OFF_PIX = OFF_COL + COL_BYTES
SLOT = 3840 * 2160 * 4
OFF_RENDER = OFF_PIX + SLOT * 3
RENDER_BYTES = 64 << 20
SIZE = OFF_RENDER + RENDER_BYTES
IN_KEY, IN_GIVE, IN_GIVE_DATA = 1, 9, 10
KEY_W, KEY_A, KEY_S, KEY_D, KEY_LCTRL = 26, 4, 22, 7, 224  # SDL scancodes
FOLLOW_FROM, STOP_AT, SPRINT_FROM = 1.5, 0.6, 8.0

kernel32 = ctypes.windll.kernel32
kernel32.GetTickCount64.restype = ctypes.c_uint64


def tick():
    return kernel32.GetTickCount64()


def cstr(m, at, n):
    raw = bytes(m[at:at + n])
    return raw.split(b"\0", 1)[0].decode("utf-8", "replace")


class HostLink:
    """Read-only view of the host's link: its player, its Minecraft's shared-world link, its Valheim's ground."""

    def __init__(self):
        self.m = mmap.mmap(-1, SIZE, tagname=HOST)
        self.read_at = self.col_head() - min(self.col_head(), 8 << 20)  # the ground of late, then whatever comes

    def mc(self):
        flags, x, y, z = struct.unpack_from("<Iddd", self.m, OFF_MC + 4)
        yaw, pitch = struct.unpack_from("<ff", self.m, OFF_MC + 0x20)
        mp_state = struct.unpack_from("<I", self.m, OFF_MC + 0xD0)[0]
        return dict(flags=flags, pos=(x, y, z), yaw=yaw, pitch=pitch, mp_state=mp_state, link=cstr(self.m, OFF_MC + 0xD4, 44))

    def world_id(self):
        return struct.unpack_from("<I", self.m, OFF_VAL + 8)[0]

    def col_head(self):
        return struct.unpack_from("<Q", self.m, OFF_COL)[0]

    def new_collision(self):
        out = []
        head = self.col_head()
        if head - self.read_at > COL_DATA or head < self.read_at:
            self.read_at = head
            return out
        while self.read_at < head:
            pos = self.read_at % COL_DATA
            typ, n = struct.unpack_from("<II", self.m, OFF_COL + 0x80 + pos)
            if typ == 0:
                self.read_at += COL_DATA - pos
                continue
            start = OFF_COL + 0x80 + pos + 8
            out.append((typ, bytes(self.m[start:start + n])))
            self.read_at += (8 + n + 7) & ~7
        return out


class Link:
    """The guest's link, served as Valheim would."""

    def __init__(self):
        self.m = mmap.mmap(-1, SIZE, tagname=NAME)
        for off, n in ((0, 0x100), (OFF_VAL, 0x100), (OFF_OVL, 0x100), (OFF_IN, 0x80), (OFF_COL, 0x80), (OFF_ACTORS, 0x40),
                       (OFF_EVENTS, 0x80), (OFF_RENDER, 0x80)):
            self.m[off:off + n] = bytes(n)
        struct.pack_into("<IIII", self.m, 0, MAGIC, VERSION, os.getpid(), 0)
        self.front = 2
        self.seq = 0
        self.col_head = 0

    def heartbeat(self):
        struct.pack_into("<Q", self.m, 0x10, tick())
        # Rings Minecraft writes: events (printed), render (dropped).
        head, tail = struct.unpack_from("<Q", self.m, OFF_EVENTS)[0], struct.unpack_from("<Q", self.m, OFF_EVENTS + 0x40)[0]
        while tail < head:
            typ, form, a, b, c, d, flags, weapon = struct.unpack_from("<IIffffII", self.m, OFF_EVENTS + 0x80 + (tail % 512) * 32)
            print(f"  event for the guest's Valheim: type={typ} form={form:08X} a={a:.2f} b={b:.2f} c={c:.2f} d={d:.2f} flags={flags:#x} weapon={weapon}")
            tail += 1
        struct.pack_into("<Q", self.m, OFF_EVENTS + 0x40, tail)
        struct.pack_into("<Q", self.m, OFF_RENDER + 0x40, struct.unpack_from("<Q", self.m, OFF_RENDER)[0])
        state = struct.unpack_from("<I", self.m, OFF_OVL)[0]
        if state & 4:
            struct.pack_into("<I", self.m, OFF_OVL, self.front)
            self.front = state & 3

    def mc_alive(self):
        beat = struct.unpack_from("<Q", self.m, 0x18)[0]
        return beat != 0 and tick() - beat < 3000

    def mc(self):
        flags, x, y, z = struct.unpack_from("<Iddd", self.m, OFF_MC + 4)
        ack = struct.unpack_from("<I", self.m, OFF_MC + 0x30)[0]
        mp_state = struct.unpack_from("<I", self.m, OFF_MC + 0xD0)[0]
        return dict(flags=flags, pos=(x, y, z), ack=ack, mp_state=mp_state, link=cstr(self.m, OFF_MC + 0xD4, 44))

    def write_val(self, flags, world_id, epoch, pos, yaw, pitch, teleport_seq, mp_mode, mp_seq, mp_link):
        self.seq += 2
        struct.pack_into("<I", self.m, OFF_VAL, self.seq - 1)
        struct.pack_into("<IIIdddffIIIffIII", self.m, OFF_VAL + 4, flags, world_id, epoch, pos[0], pos[1], pos[2], yaw, pitch, teleport_seq,
                         1280, 720, 12.0, 70.0, 1, mp_mode, mp_seq)
        link = mp_link.encode()[:63]
        self.m[OFF_VAL + 0x50:OFF_VAL + 0x90] = link + bytes(64 - len(link))
        struct.pack_into("<I", self.m, OFF_VAL, self.seq)

    def push_input(self, typ, code, a=0, b=0, c=0):
        a, b, c = (v - (1 << 32) if v >= 1 << 31 else v for v in (a, b, c))
        head = struct.unpack_from("<Q", self.m, OFF_IN)[0]
        struct.pack_into("<HHiii", self.m, OFF_IN + 0x80 + (head % 4096) * 16, typ, code, a, b, c)
        struct.pack_into("<Q", self.m, OFF_IN, head + 1)

    def give(self, item, count):
        data = item.encode()
        self.push_input(IN_GIVE, 0, count, len(data), 0)
        data += bytes(-len(data) % 12)
        for i in range(0, len(data), 12):
            self.push_input(IN_GIVE_DATA, 0, *struct.unpack_from("<III", data, i))

    def write_col(self, typ, payload):
        msg = (8 + len(payload) + 7) & ~7
        pos = self.col_head % COL_DATA
        if pos + msg > COL_DATA:
            struct.pack_into("<II", self.m, OFF_COL + 0x80 + pos, 0, 0)
            self.col_head += COL_DATA - pos
            pos = 0
        base = OFF_COL + 0x80 + pos
        struct.pack_into("<II", self.m, base, typ, len(payload))
        self.m[base + 8:base + 8 + len(payload)] = payload
        self.col_head += msg
        struct.pack_into("<Q", self.m, OFF_COL, self.col_head)


def yaw_towards(frm, to):
    return -math.degrees(math.atan2(to[0] - frm[0], to[2] - frm[2]))


def main():
    seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 3600
    angle = math.radians(float(sys.argv[2]) if len(sys.argv) > 2 else 0.0)
    radius = float(sys.argv[3]) if len(sys.argv) > 3 else 3.0
    slot = (math.cos(angle) * radius, math.sin(angle) * radius)
    host = HostLink()
    h = host.mc()
    if not h["flags"] & 1:
        print("the host's Minecraft isn't in its world yet; start there first")
        return
    link = Link()
    epoch = int(time.time()) % 100000 + 2
    cleared = False
    tseq = 1
    spawn = (h["pos"][0] + slot[0], h["pos"][1] + 1.0, h["pos"][2] + slot[1])
    held = {k: False for k in (KEY_W, KEY_A, KEY_S, KEY_D, KEY_LCTRL)}
    moving = False
    start = time.time()
    last_print = 0.0
    forwarded = 0
    gave = False
    mp_link, mp_seq = "", 0

    def key(code, down):
        if held[code] != down:
            held[code] = down
            link.push_input(IN_KEY, code, 1 if down else 0)

    print(f"host at ({h['pos'][0]:.1f}, {h['pos'][1]:.1f}, {h['pos'][2]:.1f}); waiting for the guest's Minecraft")
    while time.time() - start < seconds:
        link.heartbeat()
        h = host.mc()
        # What the friends' Valheims agreed: the host's shared world, once it has a link.
        if h["mp_state"] & 1 and h["link"] and h["link"] != mp_link:
            mp_link, mp_seq = h["link"], mp_seq + 1
            print(f"the host's world is open to friends at {mp_link}: joining it")
        if link.mc_alive() and not cleared:
            link.write_col(1, struct.pack("<I", epoch))
            cleared = True
        for typ, payload in host.new_collision():
            if typ in (2, 3) and cleared and len(payload) >= 32:
                link.write_col(typ, payload[:24] + struct.pack("<I", epoch) + payload[28:])
                forwarded += 1
            elif typ == 4 and cleared:
                link.write_col(typ, payload)
                forwarded += 1
        g = link.mc()
        in_world = g["flags"] & 1 and g["ack"] == tseq
        hpos = h["pos"]
        if in_world and (g["pos"][1] < hpos[1] - 20.0 or math.hypot(hpos[0] - g["pos"][0], hpos[2] - g["pos"][2]) > 64.0):
            spawn = (hpos[0] + slot[0], hpos[1] + 1.0, hpos[2] + slot[1])
            tseq += 1
            in_world = False
            print(f"guest lost at ({g['pos'][0]:.1f}, {g['pos'][1]:.1f}, {g['pos'][2]:.1f}); teleporting it back beside the host")
        yaw, pitch = h["yaw"], h["pitch"]
        if in_world and g["mp_state"] & 2:
            target = (hpos[0] + slot[0], hpos[1], hpos[2] + slot[1])
            dist = math.hypot(target[0] - g["pos"][0], target[2] - g["pos"][2])
            moving = dist > FOLLOW_FROM or (moving and dist > STOP_AT)
            diff = (yaw_towards(g["pos"], target) - yaw + 180.0) % 360.0 - 180.0
            key(KEY_W, moving and abs(diff) < 67.5)
            key(KEY_S, moving and abs(diff) > 112.5)
            key(KEY_D, moving and 22.5 < diff < 157.5)
            key(KEY_A, moving and -157.5 < diff < -22.5)
            key(KEY_LCTRL, held[KEY_W] and abs(diff) < 30.0 and dist > SPRINT_FROM)
            if not gave:
                gave = True
                link.give("minecraft:oak_log", 5)
                link.give("valcraft:kale_seeds", 3)
                print("guest picked up Valheim loot: 5 oak logs, 3 kale seeds")
        link.write_val(1, host.world_id(), epoch, spawn, yaw, pitch, tseq, 2 if mp_link else 0, mp_seq, mp_link)
        if time.time() - last_print > 5:
            last_print = time.time()
            p = g["pos"]
            print(f"t={time.time() - start:6.1f} guest {'in the shared world' if g['mp_state'] & 2 else 'in world' if in_world else 'waiting'} at "
                  f"({p[0]:.1f}, {p[1]:.1f}, {p[2]:.1f}) [{g['link']}], host at ({hpos[0]:.1f}, {hpos[1]:.1f}, {hpos[2]:.1f}); "
                  f"{forwarded} ground messages copied; walking {moving}")
        time.sleep(0.01)
    for code in list(held):
        key(code, False)


if __name__ == "__main__":
    main()
