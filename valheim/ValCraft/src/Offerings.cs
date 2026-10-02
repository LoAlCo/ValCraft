using UnityEngine;

namespace ValCraft
{
    // Valheim's altars, boss item stands and locked doors from Minecraft. In Valheim you offer an
    // item by picking it in the inventory and using it on the altar, but while Minecraft drives the
    // Valheim inventory can't be opened. So the use key (G) offers the right item itself: Eikthyr's
    // deer trophies, the Elder's ancient seeds, Bonemass' withered bones, Moder's eggs, Yagluth's
    // fuling totems, Fader's bells, crypt and Sealbreaker keys, ...
    //
    // The item comes from the Valheim inventory (where items without a Minecraft counterpart land
    // when picked up) or, through the loot table in reverse, from Minecraft's inventory: offered
    // items move into the Valheim inventory first (an altar takes them when the boss appears); a
    // key is only lent for the door and goes back.
    public static class Offerings
    {
        // Returns true when the target took (or refused) an offering, so the plain interact is skipped.
        public static bool TryOffer(Player player, GameObject target)
        {
            var bowl = target.GetComponentInParent<OfferingBowl>();
            if (bowl && !bowl.m_useItemStands && bowl.m_bossItem)
                return Offer(player, bowl.m_bossItem, bowl.m_bossItems, item => bowl.UseItem(player, item), bowl.m_incompleteOfferText);

            var stand = target.GetComponentInParent<ItemStand>();
            if (stand && !stand.HaveAttachment() && stand.m_supportedItems != null && stand.m_supportedItems.Count > 0)
            {
                foreach (var supported in stand.m_supportedItems)
                    if (supported && Available(player, supported) >= 1)
                        return Offer(player, supported, 1, item => stand.UseItem(player, item), null);
                return false;  // nothing that fits: Valheim's own message
            }

            var door = target.GetComponentInParent<Door>();
            if (door && door.m_keyItem && Valheim(player, door.m_keyItem) == 0 && Mc(door.m_keyItem, 1, out string id, out int need))
            {
                // Lend Minecraft's key for the door, then take it back out of the Valheim inventory.
                var inv = player.GetInventory();
                if (!inv.AddItem(door.m_keyItem.gameObject, 1)) return false;
                bool used = door.Interact(player, false, false);
                inv.RemoveItem(door.m_keyItem.m_itemData.m_shared.m_name, 1);
                return used;
            }
            return false;
        }

        delegate bool Use(ItemDrop.ItemData item);

        static bool Offer(Player player, ItemDrop wanted, int count, Use use, string incompleteText)
        {
            var inv = player.GetInventory();
            string name = wanted.m_itemData.m_shared.m_name;
            int have = Valheim(player, wanted);
            int fromMc = 0;
            string mcId = null;
            if (have < count)
            {
                int missing = count - have;
                if (!Mc(wanted, missing, out string id, out int mcNeed))
                {
                    Shortfall(player, wanted, have, count, incompleteText);
                    return true;
                }
                // Move them over from Minecraft: the altar checks and takes them in the Valheim inventory.
                if (!inv.AddItem(wanted.gameObject, missing))
                {
                    player.Message(MessageHud.MessageType.Center, "$inventory_full");
                    return true;
                }
                Loot.Take(id, mcNeed);
                BuildTools.Spent(id, mcNeed);
                fromMc = mcNeed;
                mcId = id;
                Plugin.Log($"offering: {mcNeed} x {id} from Minecraft as {missing} x {wanted.name}");
            }
            var item = inv.GetItem(name);
            bool used = item != null && use(item);
            if (used)
            {
                // Say where it came from: items in the Valheim inventory are otherwise out of sight.
                int fromValheim = Mathf.Min(have, count);
                string itemName = Localization.instance.Localize(name);
                string text = fromMc > 0 && fromValheim > 0 ? $"ValCraft: offered {fromMc} {BuildTools.McName(mcId)} from Minecraft and {fromValheim} {itemName} from the Valheim inventory"
                    : fromMc > 0 ? $"ValCraft: offered {fromMc} {BuildTools.McName(mcId)} from Minecraft"
                    : $"ValCraft: offered {fromValheim} {itemName} from the Valheim inventory";
                Plugin.Message(text);
            }
            return used;
        }

        static int Valheim(Player player, ItemDrop wanted) => player.GetInventory().CountItems(wanted.m_itemData.m_shared.m_name);

        // Valheim's count plus what Minecraft holds of the item's counterpart.
        static int Available(Player player, ItemDrop wanted)
        {
            int n = Valheim(player, wanted);
            if (Loot.TryMap(wanted.name, out string id, out float per) && per > 0f)
                n += Mathf.FloorToInt(BuildTools.McCount(id) / per + 1e-4f);
            return n;
        }

        // Does Minecraft hold enough of the counterpart for `count` of the item?
        static bool Mc(ItemDrop wanted, int count, out string id, out int need)
        {
            need = 0;
            if (!Loot.TryMap(wanted.name, out id, out float per)) return false;
            need = Mathf.CeilToInt(count * per - 1e-4f);
            return BuildTools.Creative || BuildTools.McCount(id) >= need;
        }

        static void Shortfall(Player player, ItemDrop wanted, int have, int count, string incompleteText)
        {
            int total = Available(player, wanted);
            string what = Loot.TryMap(wanted.name, out string id, out _) ? BuildTools.McName(id) : Localization.instance.Localize(wanted.m_itemData.m_shared.m_name);
            string prefix = string.IsNullOrEmpty(incompleteText) ? "" : Localization.instance.Localize(incompleteText) + ": ";
            player.Message(MessageHud.MessageType.Center, $"{prefix}{what} {total} / {count}");
        }
    }
}
