using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using ValCraft.Link;

namespace ValCraft
{
    // Valheim loot into the Minecraft inventory. When the puppet picks up a Valheim item that has a
    // Minecraft counterpart (BepInEx/config/ValCraft.loot.txt), Minecraft gets that item instead and
    // the Valheim one is removed from the world. Items without one (trophies, Valheim gear, quest
    // items) go into Valheim's own inventory as before.
    public static class Loot
    {
        // Valheim prefab -> (Minecraft item id, Minecraft items per Valheim item)
        static readonly Dictionary<string, (string id, float per)> _map = new Dictionary<string, (string, float)>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, float> _remainder = new Dictionary<string, float>();
        static string _path;

        const string Defaults = @"# ValCraft loot bridge: Valheim item (prefab name) = Minecraft item id [x per Valheim item]
# Picked-up Valheim items listed here go into your Minecraft inventory instead. Remove a line to
# keep that item in Valheim. ""x 0.5"" gives one Minecraft item per two Valheim ones.

# wood
Wood = minecraft:oak_log
RoundLog = minecraft:spruce_log
FineWood = minecraft:birch_log
ElderBark = minecraft:dark_oak_log
YggdrasilWood = minecraft:cherry_log
Blackwood = minecraft:mangrove_log
Resin = minecraft:slime_ball
# stone and minerals
Stone = minecraft:cobblestone
Flint = minecraft:flint
Obsidian = minecraft:obsidian
Crystal = minecraft:amethyst_shard
Coal = minecraft:coal
Grausten = minecraft:blackstone
BlackMarble = minecraft:deepslate
# ores and metals
CopperOre = minecraft:raw_copper
CopperScrap = minecraft:raw_copper
TinOre = minecraft:raw_iron
IronScrap = minecraft:raw_iron
IronOre = minecraft:raw_iron
SilverOre = minecraft:raw_gold
Copper = minecraft:copper_ingot
Tin = minecraft:iron_nugget x 3
Bronze = minecraft:copper_ingot
Iron = minecraft:iron_ingot
Silver = minecraft:gold_ingot
BlackMetal = minecraft:netherite_scrap
Coins = minecraft:gold_nugget
Ruby = minecraft:emerald
Amber = minecraft:honeycomb
AmberPearl = minecraft:heart_of_the_sea x 0.1
# animals
LeatherScraps = minecraft:leather
DeerHide = minecraft:leather
TrollHide = minecraft:leather x 2
WolfPelt = minecraft:white_wool
LoxPelt = minecraft:brown_wool x 2
Feathers = minecraft:feather
BoneFragments = minecraft:bone
Chitin = minecraft:prismarine_shard
Guck = minecraft:slime_ball
Entrails = minecraft:rotten_flesh
Ooze = minecraft:slime_ball
# food
RawMeat = minecraft:porkchop
DeerMeat = minecraft:beef
NeckTail = minecraft:cod
FishRaw = minecraft:cod
WolfMeat = minecraft:mutton
LoxMeat = minecraft:beef x 2
ChickenMeat = minecraft:chicken
HareMeat = minecraft:rabbit
SerpentMeat = minecraft:salmon x 4
Raspberry = minecraft:sweet_berries
Blueberries = minecraft:sweet_berries
Cloudberry = minecraft:glow_berries
Mushroom = minecraft:red_mushroom
MushroomYellow = minecraft:brown_mushroom
Carrot = minecraft:carrot
Turnip = minecraft:beetroot
Onion = minecraft:potato
Barley = minecraft:wheat
Flax = minecraft:string
Honey = minecraft:honey_bottle
Egg = minecraft:egg
# plants
Dandelion = minecraft:dandelion
Thistle = minecraft:blue_orchid
CarrotSeeds = minecraft:wheat_seeds
TurnipSeeds = minecraft:beetroot_seeds
";

        public static void Load()
        {
            _path = Path.Combine(Paths.ConfigPath, "ValCraft.loot.txt");
            try
            {
                if (!File.Exists(_path)) File.WriteAllText(_path, Defaults);
                _map.Clear();
                foreach (var raw in File.ReadAllLines(_path))
                {
                    var line = raw.Trim();
                    int hash = line.IndexOf('#');
                    if (hash >= 0) line = line.Substring(0, hash).Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string valheim = line.Substring(0, eq).Trim();
                    string rest = line.Substring(eq + 1).Trim();
                    float per = 1f;
                    int x = rest.IndexOf(" x ", StringComparison.Ordinal);
                    if (x > 0)
                    {
                        float.TryParse(rest.Substring(x + 3).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out per);
                        rest = rest.Substring(0, x).Trim();
                    }
                    if (rest.Length == 0 || rest.Length > 60 || per <= 0f) continue;
                    _map[valheim] = (rest, per);
                }
                Plugin.Log($"loot bridge: {_map.Count} Valheim items go to Minecraft ({_path})");
            }
            catch (Exception e)
            {
                Plugin.Warn("loot bridge: couldn't read " + _path + ": " + e.Message);
            }
        }

        static string PrefabName(ItemDrop drop)
        {
            var prefab = drop.m_itemData?.m_dropPrefab;
            string name = prefab ? prefab.name : drop.gameObject.name;
            int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return (clone >= 0 ? name.Substring(0, clone) : name).Trim();
        }

        // From the Humanoid.Pickup patch: true if Minecraft took the item (Valheim's pickup is skipped).
        public static bool TryTake(Player player, ItemDrop drop, bool autoPickupDelay)
        {
            if (!Puppet.Puppeting || !Shm.Valid || drop == null || drop.m_itemData == null) return false;
            if (drop.m_itemData.m_shared.m_questItem) return false;
            string name = PrefabName(drop);
            if (!_map.TryGetValue(name, out var target)) return false;
            if (!drop.CanPickup(autoPickupDelay)) return false;
            int stack = Math.Max(1, drop.m_itemData.m_stack);
            _remainder.TryGetValue(name, out float carry);
            float total = stack * target.per + carry;
            int count = (int)Math.Floor(total + 1e-4f);
            _remainder[name] = total - count;
            if (count > 0) Give(target.id, count);
            ZNetScene.instance.Destroy(drop.gameObject);
            player.m_pickupEffects.Create(player.transform.position, UnityEngine.Quaternion.identity);
            Plugin.Log($"loot: {stack} x {name} -> {count} x {target.id}");
            return true;
        }

        // kInGive {code 0, a = count, b = id length} then ceil(len / 12) kInGiveData events with
        // 12 bytes of the UTF-8 id each in a, b, c.
        static void Give(string id, int count)
        {
            var bytes = Encoding.UTF8.GetBytes(id);
            Shm.PushInput(Proto.InGive, 0, count, bytes.Length, 0);
            for (int i = 0; i < bytes.Length; i += 12)
            {
                int Word(int at)
                {
                    int w = 0;
                    for (int k = 0; k < 4; k++) if (at + k < bytes.Length) w |= bytes[at + k] << (8 * k);
                    return w;
                }
                Shm.PushInput(Proto.InGiveData, 0, Word(i), Word(i + 4), Word(i + 8));
            }
        }
    }
}
