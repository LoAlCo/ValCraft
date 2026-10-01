"""Builds both halves of ValCraft and packs a release into dist/.

  ValCraft-Minecraft.zip            the Minecraft Valheim starts: portable Prism Launcher with a ready
                                    "ValCraft" instance (Minecraft 26.3, Fabric, Fabric API, ValCraft)
  LoAlCo-ValCraft-<version>.zip     Thunderstore/Gale package: the BepInEx plugin + ValCraft-Minecraft.zip
  valcraft-fabric-<version>.jar     the Minecraft mod on its own (for your own launcher)

  python tools/package.py [--no-build] [--deploy]

--deploy also copies ValCraft-Minecraft.zip next to the plugin in the MC-V2 Gale profile, so the
dev Valheim starts the bundled Minecraft itself (close the gradlew runClient one first).
Pinned downloads are checked against their hashes. Nothing from Minecraft or Valheim is packed.
"""
import hashlib
import json
import os
import shutil
import struct
import subprocess
import sys
import urllib.request
import zipfile
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CACHE = os.path.join(ROOT, ".tools", "cache")
DIST = os.path.join(ROOT, "dist")
PROFILE = os.path.join(os.environ.get("APPDATA", ""), "com.kesomannen.gale", "valheim", "profiles", "MC-V2")

PRISM_VERSION = "11.1.1"
PRISM_ZIP = f"PrismLauncher-Windows-MSVC-Portable-{PRISM_VERSION}.zip"
PRISM_URL = f"https://github.com/PrismLauncher/PrismLauncher/releases/download/{PRISM_VERSION}/{PRISM_ZIP}"
PRISM_SHA256 = "ab35a770fb06d89d2ccc098079db5db329fb4e68f42b72babd8b095efde3d2d7"
PRISM_LICENSE_URL = f"https://raw.githubusercontent.com/PrismLauncher/PrismLauncher/{PRISM_VERSION}/LICENSE"
FABRIC_API_JAR = "fabric-api-0.161.0+26.3.jar"
FABRIC_API_URL = "https://cdn.modrinth.com/data/P7dR8mSH/versions/bNnaTiuM/fabric-api-0.161.0%2B26.3.jar"
FABRIC_API_SHA512 = "ed6b2586d6fde11fde8472f5a527c51e99b67026e46f94d4bfd85e7e28ce5ee299173ee16ad576ceb51f39f98d30a811086a6deb1a86a524859cc16e12da109d"


def version():
    for line in open(os.path.join(ROOT, "fabric", "gradle.properties"), encoding="utf-8"):
        if line.startswith("version="):
            return line.split("=", 1)[1].strip()
    raise SystemExit("no version= in fabric/gradle.properties")


def pinned(url, name, algorithm=None, digest=None):
    path = os.path.join(CACHE, name)
    os.makedirs(CACHE, exist_ok=True)
    if not os.path.exists(path):
        print("downloading", url)
        with urllib.request.urlopen(url) as r, open(path + ".part", "wb") as f:
            shutil.copyfileobj(r, f)
        os.replace(path + ".part", path)
    if algorithm:
        h = hashlib.new(algorithm, open(path, "rb").read()).hexdigest()
        if h.lower() != digest.lower():
            os.remove(path)
            raise SystemExit(f"{name} doesn't match its pinned {algorithm} hash ({h})")
    return path


def run(cmd, cwd, env=None):
    print(">", " ".join(cmd))
    subprocess.run(cmd, cwd=cwd, check=True, env=env, shell=(os.name == "nt"))


def zip_folder(path, folder, extra=None):
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        for base, _, files in os.walk(folder):
            for name in sorted(files):
                full = os.path.join(base, name)
                z.write(full, os.path.relpath(full, folder).replace("\\", "/"))
        for arc, src in (extra or {}).items():
            z.write(src, arc)


def icon_png(path):
    """A 256x256 icon drawn in code: a Minecraft grass block face on a Valheim night sky."""
    w = h = 256
    rows = []
    for y in range(h):
        row = bytearray([0])
        for x in range(w):
            px, py = x // 16, y // 16
            noise = ((px * 73856093) ^ (py * 19349663)) & 0x1F
            if 3 <= px <= 12 and 4 <= py <= 13:
                if py <= 6 or (py == 7 and (px * 7 + 3) % 3 == 0):
                    c = (70 + noise, 140 + noise, 45)       # grass top
                else:
                    c = (120 + noise, 85 + noise // 2, 55)  # dirt
            else:
                t = y / h
                c = (int(20 + 40 * t), int(30 + 50 * t), int(60 + 70 * t))  # dusk sky
            row += bytes(c) + b"\xff"
        rows.append(bytes(row))
    raw = zlib.compress(b"".join(rows), 9)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + chunk(b"IDAT", raw) + chunk(b"IEND", b""))


def main():
    args = set(sys.argv[1:])
    ver = version()
    if "--no-build" not in args:
        env = dict(os.environ)
        jdk = r"C:\Program Files\Eclipse Adoptium\jdk-25.0.3.9-hotspot"
        if os.path.isdir(jdk):
            env["JAVA_HOME"] = jdk
        run(["gradlew.bat" if os.name == "nt" else "./gradlew", "build", "--no-daemon"], os.path.join(ROOT, "fabric"), env)
        run(["dotnet", "build", "-c", "Release"], os.path.join(ROOT, "valheim", "ValCraft"))

    jar = os.path.join(ROOT, "fabric", "build", "libs", f"valcraft-{ver}.jar")
    dll = os.path.join(ROOT, "valheim", "ValCraft", "bin", "Release", "netstandard2.1", "ValCraft.dll")
    for f in (jar, dll):
        if not os.path.exists(f):
            raise SystemExit(f"missing {f} (build first, or drop --no-build)")
    prism = pinned(PRISM_URL, PRISM_ZIP, "sha256", PRISM_SHA256)
    fabric_api = pinned(FABRIC_API_URL, FABRIC_API_JAR, "sha512", FABRIC_API_SHA512)
    prism_license = pinned(PRISM_LICENSE_URL, f"PrismLauncher-{PRISM_VERSION}-LICENSE.txt")

    if os.path.isdir(DIST):
        shutil.rmtree(DIST)
    os.makedirs(DIST)

    # The bundled Minecraft: Prism (portable), the ValCraft instance and its mods, Prism's defaults.
    bundle = os.path.join(DIST, "bundle")
    shutil.copytree(os.path.join(ROOT, "tools", "minecraft-bundle"), bundle)
    with zipfile.ZipFile(prism) as z:
        z.extractall(os.path.join(bundle, "Prism"))
    shutil.copy(prism_license, os.path.join(bundle, "Prism", "LICENSE-PrismLauncher.txt"))
    third = os.path.join(bundle, "Prism", "THIRD-PARTY.txt")
    open(third, "w", encoding="utf-8").write(open(third, encoding="utf-8").read().replace("{PRISM_VERSION}", PRISM_VERSION))
    mods = os.path.join(bundle, "Prism", "instances", "ValCraft", ".minecraft", "mods")
    os.makedirs(mods)
    shutil.copy(fabric_api, mods)
    shutil.copy(jar, os.path.join(mods, f"valcraft-{ver}.jar"))
    open(os.path.join(bundle, "bundle-version.txt"), "w").write(f"ValCraft {ver}, Prism Launcher {PRISM_VERSION}, {FABRIC_API_JAR}")
    mc_zip = os.path.join(DIST, "ValCraft-Minecraft.zip")
    zip_folder(mc_zip, bundle)
    shutil.rmtree(bundle)

    # Thunderstore / Gale package.
    pkg = os.path.join(DIST, "package")
    plugin_dir = os.path.join(pkg, "plugins", "ValCraft")
    os.makedirs(plugin_dir)
    shutil.copy(dll, plugin_dir)
    shutil.copy(mc_zip, plugin_dir)
    shutil.copy(os.path.join(ROOT, "NOTICE.md"), plugin_dir)
    manifest = {
        "name": "ValCraft",
        "version_number": ver,
        "website_url": "https://github.com/LoAlCo/ValCraft2",
        "description": "Play Valheim as a Minecraft player: Minecraft runs alongside and drives movement, inventory, blocks and combat.",
        "dependencies": ["denikson-BepInExPack_Valheim-5.4.2350"],
    }
    json.dump(manifest, open(os.path.join(pkg, "manifest.json"), "w"), indent=2)
    shutil.copy(os.path.join(ROOT, "README.md"), os.path.join(pkg, "README.md"))
    icon_png(os.path.join(pkg, "icon.png"))
    zip_folder(os.path.join(DIST, f"LoAlCo-ValCraft-{ver}.zip"), pkg)
    shutil.rmtree(pkg)
    shutil.copy(jar, os.path.join(DIST, f"valcraft-fabric-{ver}.jar"))

    if "--deploy" in args:
        target = os.path.join(PROFILE, "BepInEx", "plugins", "ValCraft")
        os.makedirs(target, exist_ok=True)
        shutil.copy(mc_zip, target)
        print("deployed ValCraft-Minecraft.zip to", target)

    for name in sorted(os.listdir(DIST)):
        print(f"{name:40} {os.path.getsize(os.path.join(DIST, name)):>14,} bytes")


if __name__ == "__main__":
    main()
