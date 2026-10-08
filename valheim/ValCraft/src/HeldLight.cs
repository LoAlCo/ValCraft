using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // A light in Minecraft's hand (a torch, lantern, glowstone, ...: McState McHoldingLight) lights the
    // player like Valheim's own torch: a copy of the Valheim torch's flickering light, in the right
    // hand. Soul torches and lanterns glow blue. Also hands WorldRender the Valheim torch's light, so
    // Minecraft's placed torches and other flames shine like Valheim's do.
    public static class HeldLight
    {
        static GameObject _light;
        static Light _template;
        static bool _lookedUp;
        static Color _baseColor;
        static readonly Color Soul = new Color(0.35f, 0.82f, 1f);
        const float ReachScale = 1.5f;

        /** Valheim's torch light (range, intensity, shadows), or null before the game has loaded it. */
        public static Light Template
        {
            get
            {
                if (!_lookedUp && ObjectDB.instance && ZNetScene.instance)
                {
                    _lookedUp = true;
                    _template = FindLight(ZNetScene.instance.GetPrefab("piece_groundtorch")) ?? FindLight(ObjectDB.instance.GetItemPrefab("Torch"));
                    Plugin.Log(_template ? $"light: Minecraft's lights shine like Valheim's torch (range {_template.range:F1}, intensity {_template.intensity:F2}, shadows {_template.shadows})"
                                         : "light: Valheim's torch light wasn't found");
                }
                return _template;
            }
        }

        static Light FindLight(GameObject prefab) => prefab ? prefab.GetComponentInChildren<Light>(true) : null;

        // Main thread, every frame.
        public static void Frame()
        {
            var player = Player.m_localPlayer;
            uint flags = Puppet.McConnected ? Puppet.Mc.flags : 0u;
            bool want = player && Puppet.Puppeting && (flags & Proto.McHoldingLight) != 0;
            if (!want)
            {
                if (_light && _light.activeSelf) _light.SetActive(false);
                return;
            }
            var hand = player.GetComponent<VisEquipment>()?.m_rightHand;
            if (!_light)
            {
                var torch = FindLight(ObjectDB.instance ? ObjectDB.instance.GetItemPrefab("Torch") : null) ?? Template;
                if (!torch) return;
                _light = Object.Instantiate(torch.gameObject);
                _light.name = "ValCraft held light";
                foreach (var c in _light.GetComponentsInChildren<Component>(true))
                    if (!(c is Transform) && !(c is Light) && !(c is LightFlicker)) Object.Destroy(c);
                var l = _light.GetComponent<Light>();
                _baseColor = l.color;
                l.range *= ReachScale;  // reaches a little further than Valheim's own torch
            }
            var parent = hand ? hand : player.transform;
            if (_light.transform.parent != parent)
            {
                _light.transform.SetParent(parent, false);
                _light.transform.localPosition = hand ? Vector3.zero : new Vector3(0.3f, 1.4f, 0.4f);
            }
            var light = _light.GetComponent<Light>();
            if (light) light.color = (flags & Proto.McHoldingSoulLight) != 0 ? Soul : _baseColor;
            if (!_light.activeSelf) _light.SetActive(true);
        }
    }
}
