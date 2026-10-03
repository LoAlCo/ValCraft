using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValCraft
{
    // A TNT blast in rock leaves hundreds of stone drops, each its own rigidbody. Valheim only stacks
    // drops when the whole world holds over 200 of them, each one doing its own physics query once.
    // Here one pass every couple of seconds goes over every drop near the player: drops of the same
    // item within a few metres (bucketed on a grid, no physics queries) merge into one stack, as many
    // as fit. Only drops this client owns, so a friend's drops are left to them.
    public static class DropMerge
    {
        const float Interval = 2f;
        const float Range = 80f;   // metres around the player
        const float Cell = 3f;     // drops in the same or a neighbouring cell may merge
        const float Reach = 3f;

        static float _next;
        static readonly AccessTools.FieldRef<List<ItemDrop>> Instances = AccessTools.StaticFieldRefAccess<List<ItemDrop>>(AccessTools.Field(typeof(ItemDrop), "s_instances"));
        static readonly AccessTools.FieldRef<ItemDrop, ZNetView> View = AccessTools.FieldRefAccess<ItemDrop, ZNetView>("m_nview");
        static readonly Action<ItemDrop> Save = AccessTools.MethodDelegate<Action<ItemDrop>>(AccessTools.Method(typeof(ItemDrop), "Save"));

        static readonly Dictionary<(long cell, string name, int quality), List<ItemDrop>> _cells = new Dictionary<(long, string, int), List<ItemDrop>>();
        static readonly Stack<List<ItemDrop>> _spare = new Stack<List<ItemDrop>>();
        // Merged away this pass: Unity only removes a destroyed object at the end of the frame, so it
        // still looks alive here and mustn't take part again (a stack merged into it would vanish).
        static readonly HashSet<ItemDrop> _gone = new HashSet<ItemDrop>();

        public static void Frame()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            var player = Player.m_localPlayer;
            var all = Instances();
            if (!player || all == null || all.Count < 2) return;
            Vector3 centre = player.transform.position;

            foreach (var list in _cells.Values) { list.Clear(); _spare.Push(list); }
            _cells.Clear();
            foreach (var drop in all)
            {
                if (!drop) continue;
                var shared = drop.m_itemData.m_shared;
                if (shared.m_maxStackSize <= 1 || !shared.m_autoStack || drop.m_itemData.m_stack >= shared.m_maxStackSize) continue;
                var p = drop.transform.position;
                if ((p - centre).sqrMagnitude > Range * Range) continue;
                var view = View(drop);
                if (!view || !view.IsValid() || !view.IsOwner()) continue;
                var key = (CellKey(p), shared.m_name, drop.m_itemData.m_quality);
                if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = _spare.Count > 0 ? _spare.Pop() : new List<ItemDrop>();
                list.Add(drop);
            }

            int merged = 0;
            _gone.Clear();
            foreach (var kv in _cells)
            {
                var (cell, name, quality) = kv.Key;
                foreach (var into in kv.Value)
                {
                    if (!into || _gone.Contains(into) || !View(into) || into.m_itemData.m_stack >= into.m_itemData.m_shared.m_maxStackSize) continue;
                    bool changed = false;
                    // the drop's own cell and its eight neighbours (same height band)
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!_cells.TryGetValue((Offset(cell, dx, dz), name, quality), out var others)) continue;
                            foreach (var other in others)
                            {
                                if (other == into || !other || _gone.Contains(other) || !View(other) || !View(other).IsValid()) continue;
                                if ((other.transform.position - into.transform.position).sqrMagnitude > Reach * Reach) continue;
                                int room = into.m_itemData.m_shared.m_maxStackSize - into.m_itemData.m_stack;
                                if (room <= 0) break;
                                if (other.m_itemData.m_stack > room) continue;
                                into.m_itemData.m_stack += other.m_itemData.m_stack;
                                _gone.Add(other);
                                View(other).Destroy();
                                changed = true;
                                merged++;
                            }
                        }
                    if (changed) Save(into);
                }
            }
            if (merged > 0 && Plugin.Diagnostics.Value) Plugin.Log($"drops: merged {merged} into stacks");
        }

        // Cells are 3 m square and 3 m tall, packed into a long.
        static long CellKey(Vector3 p)
        {
            long x = Mathf.FloorToInt(p.x / Cell), y = Mathf.FloorToInt(p.y / Cell), z = Mathf.FloorToInt(p.z / Cell);
            return ((x & 0x1FFFFF) << 42) | ((y & 0x1FFFFF) << 21) | (z & 0x1FFFFF);
        }

        static long Offset(long cell, int dx, int dz)
        {
            long x = ((cell >> 42) & 0x1FFFFF) + dx, y = (cell >> 21) & 0x1FFFFF, z = (cell & 0x1FFFFF) + dz;
            return ((x & 0x1FFFFF) << 42) | (y << 21) | (z & 0x1FFFFF);
        }
    }
}
