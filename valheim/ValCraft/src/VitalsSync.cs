using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // [Valheim Features] FoodAndHealth: Valheim's food and health for Minecraft's. Valheim stays in
    // charge of both: eating in Minecraft is Valheim eating the matching Valheim food (Combat's
    // EvEat; Minecraft picks which, ValheimVitals.java), with Valheim's rules (three foods, no second
    // helping before half its time is gone), Valheim's max health (base plus what the foods give,
    // tapering off as they run out) becomes Minecraft's (ValState.maxHealth), and Valheim's healing
    // (food regen every 10 s, meads, Rested) heals the Minecraft player (Proto.InHeal). Minecraft's
    // hunger stops draining and stops healing. Off ("Minecraft"): Minecraft's hearts and hunger as usual.
    public static unsafe class VitalsSync
    {
        public static ConfigEntry<string> FoodAndHealth;

        public static bool ValheimFood => FoodAndHealth == null || FoodAndHealth.Value != "Minecraft";

        static bool Active(Player player) => ValheimFood && Puppet.Puppeting && Shm.Valid && player && player == Player.m_localPlayer;

        public static void Init(ConfigFile config)
        {
            FoodAndHealth = config.Bind("Valheim Features", "FoodAndHealth", "Valheim", new ConfigDescription(
                "Valheim: food and health work as in Valheim. Eating gives max health and healing for a while (three foods at once, each on its own timer), and Minecraft's hunger no longer drains or heals. Minecraft: Minecraft's hearts and hunger.",
                new AcceptableValueList<string>("Valheim", "Minecraft")));
        }

        // Into the ValState Puppet writes every frame.
        public static void Write(ref ValState st, Player player)
        {
            if (!ValheimFood || !player) return;
            st.flags |= Proto.ValFoodValheim;
            st.maxHealth = player.GetMaxHealth();
            var foods = player.GetFoods();
            int n = Mathf.Min(3, foods.Count);
            st.foodCount = (uint)n;
            for (int i = 0; i < n; i++)
            {
                var f = foods[i];
                st.foodHash[i] = Combat.JavaHash(f.m_name ?? "");
                st.foodTime[i] = f.m_time;
                st.foodBurn[i] = f.m_item != null ? f.m_item.m_shared.m_foodBurnTime : 1f;
                st.foodHealth[i] = f.m_health;
            }
        }

        // Ate in Minecraft (the matching Valheim food).
        public static void Eat(Player player, int prefabHash)
        {
            var prefab = Combat.ValheimItemByHash(prefabHash);
            var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
            if (!drop) { Plugin.Warn($"eat: no Valheim food with hash {prefabHash}"); return; }
            var item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = 1;
            if (!player.EatFood(item))
            {
                Plugin.Log($"eat: Valheim says no to {prefab.name}");
                return;
            }
            var se = item.m_shared.m_consumeStatusEffect;
            if (se) player.GetSEMan().AddStatusEffect(se, resetTime: true);
            Plugin.Log($"eat: {prefab.name} (+{item.m_shared.m_food} health for {item.m_shared.m_foodBurnTime / 60f:0} min)");
        }

        // Eat or drink what sits in the Valheim inventory (the Valheim Items tab), the Valheim way.
        public static void EatValheimItem(Player player, int x, int y)
        {
            var inv = player.GetInventory();
            var item = inv.GetItemAt(x, y);
            if (item == null) return;
            if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable)
            {
                player.Message(MessageHud.MessageType.Center, Localization.instance.Localize(item.m_shared.m_name) + ": not something to eat");
                return;
            }
            player.ConsumeItem(inv, item, checkWorldLevel: true);
        }

        // Valheim healing the player (food regen, healing meads, ...) heals the Minecraft player instead.
        // The Viking's own health isn't what counts while Minecraft drives (its hits go to Minecraft too).
        [HarmonyPatch(typeof(Character), nameof(Character.Heal))]
        static class HealToMinecraft
        {
            static bool Prefix(Character __instance, float hp)
            {
                if (!(__instance is Player player) || !Active(player)) return true;
                if (hp > 0f) Shm.PushInput(Proto.InHeal, 0, Mathf.RoundToInt(hp * 100f));
                return false;
            }
        }
    }
}
