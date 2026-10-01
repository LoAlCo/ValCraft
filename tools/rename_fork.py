"""One-shot rename of the SkyCraft Fabric fork to ValCraft (run once from the repo root).

Only SkyCraft's own identifiers are touched; Minecraft's classes are never renamed.
"""
import os
import re
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TARGETS = [os.path.join(ROOT, "fabric"), os.path.join(ROOT, "protocol")]

CLASSES = ["SkyCraftClient", "SkyCraft", "SkyLink", "SkyTri", "SkyRay", "SkyCollision", "SkyClient", "SkyClip",
           "SkyWater", "SkyCombat", "SkyNet", "SkyAtlas", "SkyState", "SkyCollider", "SkyRayTest", "SkyFlags",
           "SkyrimActorEntity"]
RENAME = {c: c.replace("Skyrim", "Valheim").replace("Sky", "Val", 1) for c in CLASSES}

SUBS = [
    (re.compile(r"\b(" + "|".join(sorted(CLASSES, key=len, reverse=True)) + r")\b"), lambda m: RENAME[m.group(1)]),
    (re.compile(r"Skyrim"), lambda m: "Valheim"),
    (re.compile(r"skyrim"), lambda m: "valheim"),
    (re.compile(r"SKYRIM"), lambda m: "VALHEIM"),
    (re.compile(r"skycraft"), lambda m: "valcraft"),
    (re.compile(r"SkyCraft"), lambda m: "ValCraft"),
    (re.compile(r"SKYCRAFT"), lambda m: "VALCRAFT"),
    # Proto constants for the host state block (SKY_IN_GAME, kSkyInGame, ...)
    (re.compile(r"\bSKY_"), lambda m: "VAL_"),
    (re.compile(r"\bkSky(?=[A-Z])"), lambda m: "kVal"),
    (re.compile(r"\bkOffSkyState\b"), lambda m: "kOffValState"),
    (re.compile(r"\bOFF_SKY_STATE\b"), lambda m: "OFF_VAL_STATE"),
]

TEXT_EXT = {".java", ".json", ".gradle", ".properties", ".h", ".md", ".txt", ".cfg"}


def rewrite(path):
    with open(path, "r", encoding="utf-8") as f:
        src = f.read()
    out = src
    for rx, fn in SUBS:
        out = rx.sub(fn, out)
    if out != src:
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(out)
        return True
    return False


def main():
    changed = 0
    for top in TARGETS:
        for dirpath, _, files in os.walk(top):
            for name in files:
                if os.path.splitext(name)[1] in TEXT_EXT:
                    changed += rewrite(os.path.join(dirpath, name))
    # rename files, then directories (deepest first)
    for top in TARGETS:
        for dirpath, dirs, files in os.walk(top, topdown=False):
            for name in files:
                stem, ext = os.path.splitext(name)
                new = RENAME.get(stem, stem).replace("skycraft", "valcraft") + ext
                if new != name:
                    os.rename(os.path.join(dirpath, name), os.path.join(dirpath, new))
            for d in dirs:
                if d == "skycraft":
                    shutil.move(os.path.join(dirpath, d), os.path.join(dirpath, "valcraft"))
    print(f"rewrote {changed} files")


if __name__ == "__main__":
    sys.exit(main())
