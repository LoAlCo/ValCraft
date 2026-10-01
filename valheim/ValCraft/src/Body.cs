using System.Collections.Generic;
using UnityEngine;

namespace ValCraft
{
    // In Minecraft's first person the Viking's body is hidden (Minecraft draws the hand). In third
    // person the Viking stays visible for now; Minecraft's own avatar comes with the render bridge.
    public static class Body
    {
        static readonly List<Renderer> _hidden = new List<Renderer>();
        static float _timer;

        public static void Apply(Player player, bool firstPerson, bool restore = false)
        {
            if (restore || !firstPerson)
            {
                if (_hidden.Count == 0) return;
                foreach (var r in _hidden) if (r) r.forceRenderingOff = false;
                _hidden.Clear();
                return;
            }
            _timer -= Time.deltaTime;
            if (_timer > 0f && _hidden.Count > 0) return;
            _timer = 0.5f;  // equipment changes add renderers: re-scan now and then
            var visual = player.GetVisual();
            if (!visual) return;
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
                if (!r.forceRenderingOff) { r.forceRenderingOff = true; _hidden.Add(r); }
        }
    }
}
