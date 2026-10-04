"""Writes weapons.txt: every Valheim weapon and tool, as one of the shapes in weapon_shapes.txt
coloured from its own icon. Materials name the colours of each part; a weapon lists its shape and
which material each part is made of. Weapons with a look of their own are drawn in weapons_custom.txt
(which comes later, so it wins).

  python tools/item_art/make_weapons.py
"""
import os

# part colours: (light, middle, dark)
M = {
    "wood": ("b8844c", "9a6a3a", "6a4422"),
    "darkwood": ("8a6248", "6a4a3a", "3e2a20"),
    "redwood": ("a05a40", "7a3a2a", "4a2018"),
    "antler": ("e8dcc0", "c8b890", "8a7a58"),
    "stone": ("c0c0b8", "8e8e88", "5e5e5a"),
    "flint": ("dcd4c4", "a09a8e", "6a645c"),
    "bronze": ("f0c878", "c49444", "8a6428"),
    "copper": ("f0a870", "c87840", "8a4e26"),
    "iron": ("e0e4ec", "a4acb8", "6a7280"),
    "silver": ("f4f8ff", "c4ccdc", "8a94a8"),
    "blackmetal": ("70d070", "2e7a2e", "143a14"),
    "darksteel": ("a8b4c0", "5a6470", "2e343c"),
    "nord": ("eef4fc", "9aaac4", "566480"),
    "gold": ("ffe080", "e0b040", "a07820"),
    "blood": ("ff9a9a", "d03838", "7a1414"),
    "storm": ("c0f4ff", "40b8e8", "1a5a8a"),
    "frost": ("f0fcff", "a8e0f4", "5aa0c8"),
    "nature": ("c8ff90", "5ad02a", "1e6a10"),
    "fire": ("ffe0a0", "f07020", "a02810"),
    "chitin": ("fff8c0", "e8d860", "a09020"),
    "carapace": ("b8e0e8", "5a8a9a", "2a4652"),
    "crystal": ("f4f0ff", "c8b8f0", "7a68b0"),
    "skull": ("a0f0d0", "40b890", "1a5a48"),
    "bone": ("f4ecdc", "d0c4a8", "8a7c60"),
    "leather": ("a0704a", "7a4a2a", "4e2e18"),
    "wrap": ("d8c8a0", "a89870", "6a5c40"),
    "red": ("e05050", "a02424", "5a1010"),
    "black": ("5a5a60", "34343a", "1a1a1e"),
    "fur": ("b0a090", "806c5c", "4e4034"),
}

# shape: its roles -> which part each role colour comes from:
#   blade e b d = (light, middle, dark) of "blade"; guard g G = (middle, dark) of "guard";
#   grip h H = (middle, dark) of "grip"; pommel p = light of "pommel"; head a m M = "head";
#   shaft w W = (middle, dark) of "shaft"; spikes s = light of "spikes".
ROLES = {
    "e": ("blade", 0), "b": ("blade", 1), "d": ("blade", 2),
    "g": ("guard", 1), "G": ("guard", 2), "h": ("grip", 1), "H": ("grip", 2), "p": ("pommel", 0),
    "a": ("head", 0), "m": ("head", 1), "M": ("head", 2), "w": ("shaft", 1), "W": ("shaft", 2),
    "s": ("spikes", 0),
}

# name, Valheim prefab, shape, parts {part: material}, outline
W = []


def w(name, prefab, shape, outline="1a1410", **parts):
    W.append((name, prefab, shape, parts, outline))


def sword(name, prefab, blade, guard="iron", grip="leather", pommel=None, shape="sword", outline="1a1410"):
    w(name, prefab, shape, outline, blade=blade, guard=guard, grip=grip, pommel=pommel or guard)


def hafted(name, prefab, shape, head, shaft="wood", spikes=None, outline="1a1410", **extra):
    w(name, prefab, shape, outline, head=head, shaft=shaft, spikes=spikes or head, **extra)


def polearm(name, prefab, shape, blade, shaft="wood", guard=None, outline="1a1410"):
    w(name, prefab, shape, outline, blade=blade, shaft=shaft, guard=guard or blade)


# ---- swords --------------------------------------------------------------------------------
sword("wooden_sword", "SwordWood", "wood", guard="wood", grip="darkwood")
sword("bronze_sword", "SwordBronze", "bronze", guard="bronze", grip="leather")
sword("iron_sword", "SwordIron", "iron", guard="iron", grip="leather")
sword("silver_sword", "SwordSilver", "silver", guard="silver", grip="wrap")
sword("black_metal_sword", "SwordBlackmetal", "blackmetal", guard="black", grip="red")
sword("mistwalker", "SwordMistwalker", "darksteel", guard="darksteel", grip="red")
sword("dyrnwyn", "SwordDyrnwyn", "fire", guard="black", grip="black")
sword("nidhogg", "SwordNiedhogg", "darksteel", guard="black", grip="black")
sword("nidhogg_the_bleeding", "SwordNiedhoggBlood", "blood", guard="black", grip="black")
sword("nidhogg_the_thundering", "SwordNiedhoggLightning", "storm", guard="black", grip="black")
sword("nidhogg_the_primal", "SwordNiedhoggNature", "nature", guard="black", grip="black")
sword("nord_sword", "SwordGold", "nord", guard="gold", grip="leather")
sword("thunderblood_sword", "SwordGold_BloodLightning", "blood", guard="gold", grip="leather")
sword("frostfire_sword", "SwordGold_FrostFire", "frost", guard="gold", grip="leather")
sword("krom", "THSwordKrom", "crystal", guard="gold", grip="wrap", shape="greatsword")
sword("slayer", "THSwordSlayer", "darksteel", guard="black", grip="black", shape="greatsword")
sword("brutal_slayer", "THSwordSlayerBlood", "blood", guard="black", grip="black", shape="greatsword")
sword("scourging_slayer", "THSwordSlayerLightning", "storm", guard="black", grip="black", shape="greatsword")
sword("primal_slayer", "THSwordSlayerNature", "nature", guard="black", grip="black", shape="greatsword")
sword("nord_greatsword", "THSwordGold", "nord", guard="gold", grip="leather", shape="greatsword")
sword("thunderblood_greatsword", "THSwordGold_BloodLightning", "blood", guard="gold", grip="leather", shape="greatsword")
sword("frostfire_greatsword", "THSwordGold_FrostFire", "frost", guard="gold", grip="leather", shape="greatsword")

# ---- knives --------------------------------------------------------------------------------
sword("flint_knife", "KnifeFlint", "flint", guard="leather", grip="leather", shape="knife")
sword("copper_knife", "KnifeCopper", "copper", guard="leather", grip="leather", shape="knife")
sword("abyssal_razor", "KnifeChitin", "chitin", guard="wrap", grip="leather", shape="curved_knife")
sword("silver_knife", "KnifeSilver", "silver", guard="silver", grip="leather", shape="knife")
sword("black_metal_knife", "KnifeBlackMetal", "blackmetal", guard="black", grip="red", shape="curved_knife")
sword("nord_dagger", "KnifeGold", "nord", guard="gold", grip="leather", shape="knife")
sword("thunderblood_dagger", "KnifeGold_BloodLightning", "blood", guard="gold", grip="leather", shape="knife")
sword("frostfire_dagger", "KnifeGold_FrostFire", "frost", guard="gold", grip="leather", shape="knife")
sword("skoll_and_hati", "KnifeSkollAndHati", "darksteel", guard="black", grip="leather", shape="curved_knife")
w("butcher_knife", "KnifeButcher", "cleaver", blade="iron", grip="wood")

# ---- axes ----------------------------------------------------------------------------------
hafted("stone_axe", "AxeStone", "axe", "stone")
hafted("flint_axe", "AxeFlint", "axe", "flint")
hafted("bronze_axe", "AxeBronze", "axe", "bronze")
hafted("iron_axe", "AxeIron", "axe", "iron")
hafted("black_metal_axe", "AxeBlackMetal", "axe", "blackmetal", shaft="redwood")
hafted("jotun_bane", "AxeJotunBane", "axe", "gold", shaft="darkwood")
hafted("nord_axe", "AxeGold", "axe", "nord", shaft="redwood")
hafted("thunderblood_axe", "AxeGold_BloodLightning", "axe", "blood", shaft="redwood")
hafted("frostfire_axe", "AxeGold_FrostFire", "axe", "frost", shaft="redwood")
hafted("early_axes", "AxeEarly", "axe", "bone", shaft="wood")
hafted("battleaxe", "Battleaxe", "battleaxe", "iron")
hafted("crystal_battleaxe", "BattleaxeCrystal", "battleaxe", "crystal", shaft="iron")
hafted("black_metal_battleaxe", "BattleaxeBlackmetal", "battleaxe", "blackmetal", shaft="redwood")
hafted("skull_splittur", "BattleaxeSkullSplittur", "battleaxe", "skull", shaft="darkwood")
hafted("nord_greataxe", "BattleaxeGold", "battleaxe", "nord", shaft="redwood")
hafted("thunderblood_greataxe", "BattleaxeGold_BloodLightning", "battleaxe", "blood", shaft="redwood")
hafted("frostfire_greataxe", "BattleaxeGold_FrostFire", "battleaxe", "frost", shaft="redwood")
hafted("berserkir_axes", "AxeBerzerkr", "battleaxe", "darksteel", shaft="redwood")
hafted("bleeding_berserkir_axes", "AxeBerzerkrBlood", "battleaxe", "blood", shaft="redwood")
hafted("thundering_berserkir_axes", "AxeBerzerkrLightning", "battleaxe", "storm", shaft="redwood")
hafted("primal_berserkir_axes", "AxeBerzerkrNature", "battleaxe", "nature", shaft="redwood")

# ---- clubs, maces, sledges -----------------------------------------------------------------
hafted("club", "Club", "club", "wood", shaft="wood", spikes="darkwood")
hafted("bronze_mace", "MaceBronze", "mace", "bronze", spikes="bronze", pommel="bronze")
hafted("iron_mace", "MaceIron", "mace", "iron", spikes="iron", pommel="iron")
hafted("frostner", "MaceSilver", "mace", "silver", spikes="frost", shaft="iron", pommel="silver")
hafted("porcupine", "MaceNeedle", "mace", "blackmetal", spikes="nature", shaft="black", pommel="red")
hafted("nord_mace", "MaceGold", "mace", "nord", spikes="gold", shaft="redwood", pommel="gold")
hafted("thunderblood_mace", "MaceGold_BloodLightning", "mace", "blood", spikes="gold", shaft="redwood", pommel="gold")
hafted("frostfire_mace", "MaceGold_FrostFire", "mace", "frost", spikes="gold", shaft="redwood", pommel="gold")
hafted("flametal_mace", "MaceEldner", "mace", "darksteel", spikes="iron", shaft="black", pommel="black")
hafted("bloodgeon", "MaceEldnerBlood", "mace", "blood", spikes="blood", shaft="black", pommel="black")
hafted("storm_star", "MaceEldnerLightning", "mace", "storm", spikes="storm", shaft="black", pommel="black")
hafted("klossen", "MaceEldnerNature", "mace", "nature", spikes="nature", shaft="black", pommel="black")
hafted("stagbreaker", "SledgeStagbreaker", "sledge", "antler")
hafted("iron_sledge", "SledgeIron", "sledge", "iron", shaft="redwood")
hafted("demolisher", "SledgeDemolisher", "sledge", "stone", shaft="redwood")
hafted("nord_sledge", "SledgeGold", "sledge", "nord", shaft="redwood")
hafted("thunderblood_sledge", "SledgeGold_BloodLightning", "sledge", "blood", shaft="redwood")
hafted("frostfire_sledge", "SledgeGold_FrostFire", "sledge", "frost", shaft="redwood")

# ---- spears and atgeirs --------------------------------------------------------------------
polearm("flint_spear", "SpearFlint", "spear", "flint", guard="wrap")
polearm("bronze_spear", "SpearBronze", "spear", "bronze")
polearm("ancient_bark_spear", "SpearElderbark", "spear", "darkwood", shaft="darkwood", guard="bronze")
polearm("fang_spear", "SpearWolfFang", "spear", "bone", guard="wrap")
polearm("abyssal_harpoon", "SpearChitin", "spear", "chitin", shaft="wrap")
polearm("carapace_spear", "SpearCarapace", "spear", "carapace", shaft="black", guard="red")
polearm("splitnir", "SpearSplitner", "spear", "darksteel", shaft="black")
polearm("splitnir_the_bleeding", "SpearSplitner_Blood", "spear", "blood", shaft="black")
polearm("splitnir_the_storming", "SpearSplitner_Lightning", "spear", "storm", shaft="black")
polearm("splitnir_the_primal", "SpearSplitner_Nature", "spear", "nature", shaft="black")
polearm("nord_spear", "SpearGold", "spear", "nord", shaft="storm", guard="gold")
polearm("thunderblood_spear", "SpearGold_BloodLightning", "spear", "blood", shaft="redwood", guard="gold")
polearm("frostfire_spear", "SpearGold_FrostFire", "spear", "frost", shaft="redwood", guard="gold")
polearm("bronze_atgeir", "AtgeirBronze", "atgeir", "bronze")
polearm("iron_atgeir", "AtgeirIron", "atgeir", "iron")
polearm("black_metal_atgeir", "AtgeirBlackmetal", "atgeir", "blackmetal", shaft="black")
polearm("himminafl", "AtgeirHimminAfl", "atgeir", "storm", shaft="nord", guard="silver")
polearm("nord_atgeir", "AtgeirGold", "atgeir", "nord", shaft="redwood", guard="gold")
polearm("thunderblood_atgeir", "AtgeirGold_BloodLightning", "atgeir", "blood", shaft="redwood", guard="gold")
polearm("frostfire_atgeir", "AtgeirGold_FrostFire", "atgeir", "frost", shaft="redwood", guard="gold")

# ---- fists ---------------------------------------------------------------------------------
w("paws_of_the_bear", "FistBjornClaw", "claws", head="fur", spikes="bone", grip="leather")
w("vilebone_maulclaws", "FistBjornUndeadClaw", "claws", head="leather", spikes="bone", grip="darkwood")
w("flesh_rippers", "FistFenrirClaw", "claws", head="fur", spikes="black", grip="leather")
w("nord_knucklechains", "FistGold", "knuckles", head="gold", spikes="silver", grip="leather")
w("thunderblood_knucklechains", "FistGold_BloodLightning", "knuckles", head="blood", spikes="gold", grip="leather")
w("frostfire_knucklechains", "FistGold_FrostFire", "knuckles", head="frost", spikes="gold", grip="leather")

# ---- tools ---------------------------------------------------------------------------------
hafted("antler_pickaxe", "PickaxeAntler", "pickaxe", "antler")
hafted("bronze_pickaxe", "PickaxeBronze", "pickaxe", "bronze")
hafted("iron_pickaxe", "PickaxeIron", "pickaxe", "iron")
hafted("black_metal_pickaxe", "PickaxeBlackMetal", "pickaxe", "blackmetal", shaft="redwood")
hafted("scythe", "Scythe", "scythe", "silver", shaft="wrap")
hafted("snow_shovel", "Shovel", "shovel", "carapace", shaft="darkwood")
hafted("cultivator", "Cultivator", "cultivator", "wrap", shaft="wood")


def main():
    out = ["# Generated by make_weapons.py: Valheim's weapons and tools as weapon_shapes.txt shapes, coloured", "# from their icons. Weapons with a look of their own are in weapons_custom.txt.", ""]
    for name, prefab, shape, parts, outline in W:
        out.append(f"@ {name} from={prefab} base={shape} outline={outline}")
        for role, (part, i) in ROLES.items():
            mat = parts.get(part)
            if mat:
                out.append(f"{role} {M[mat][i]}")
        out.append("")
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "weapons.txt")
    open(path, "w", encoding="utf-8").write("\n".join(out))
    print(f"wrote {len(W)} weapons")


if __name__ == "__main__":
    main()
