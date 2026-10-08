using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValCraft.Render
{
    // Lit materials for Minecraft's blocks, so Valheim's sun, shadows, fog and toon lighting apply.
    // Unity's Standard shader is in Valheim's build but only reachable through loaded materials, and it
    // ignores vertex colours, so Minecraft's tint (grass, leaves, water) goes into the material colour:
    // one material per quantised colour, cached.
    public static class BlockMaterials
    {
        public enum Layer { Cutout = 0, Translucent = 1 }

        static Shader _standard;
        static Texture _atlas;
        static readonly Dictionary<(int tex, int layer, uint colour, bool glow), Material> _cache = new Dictionary<(int, int, uint, bool), Material>();
        public static bool Ready => _standard != null && _atlas != null;

        public static void SetAtlas(Texture atlas)
        {
            _atlas = atlas;
            foreach (var kv in _cache) if (kv.Key.tex == 0 && kv.Value) { kv.Value.mainTexture = atlas; if (kv.Key.glow) kv.Value.SetTexture("_EmissionMap", atlas); }
        }

        // Once in the world, when Valheim's shaders are loaded.
        public static bool FindShader()
        {
            if (_standard) return true;
            foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
                if (mat && mat.shader && mat.shader.name == "Standard" && mat.shader.isSupported) { _standard = mat.shader; break; }
            if (_standard) Plugin.Log("block shader: Standard");
            return _standard;
        }

        // colour: RGBA8 (r lowest byte), already quantised by the caller.
        // tex: null for Minecraft's atlas, else an entity texture. glow: full block light (lit TNT's
        // flash, fire, explosions, glowing particles), drawn as emissive so night doesn't darken it.
        public static Material Get(Layer layer, uint colour, Texture tex = null, bool glow = false)
        {
            var key = (tex ? tex.GetInstanceID() : 0, (int)layer, colour, glow);
            if (_cache.TryGetValue(key, out var m) && m) return m;
            var texture = tex ? tex : _atlas;
            m = new Material(_standard) { name = $"valcraft_{layer}_{colour:X8}{(glow ? "_glow" : "")}", mainTexture = texture };
            var c = new Color32((byte)colour, (byte)(colour >> 8), (byte)(colour >> 16), (byte)(colour >> 24));
            m.color = c;
            m.shaderKeywords = new string[0];
            m.SetFloat("_Glossiness", 0f);
            m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            m.SetFloat("_SpecularHighlights", 0f);
            m.SetFloat("_GlossyReflections", 0f);
            if (layer == Layer.Cutout)
            {
                m.SetFloat("_Mode", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.SetInt("_SrcBlend", (int)BlendMode.One);
                m.SetInt("_DstBlend", (int)BlendMode.Zero);
                m.SetInt("_ZWrite", 1);
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                // Minecraft's translucent layer (water, stained glass, ice) blends straight alpha: Standard's "Fade".
                m.SetFloat("_Mode", 2f);
                m.EnableKeyword("_ALPHABLEND_ON");
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            if (glow)
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", texture);
                m.SetColor("_EmissionColor", (Color)c * (tex ? 0.8f : 1.15f));  // shining blocks a little brighter than lit TNT's flash
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            _cache[key] = m;
            return m;
        }

        // Minecraft's vertex colour is tint x ambient occlusion. Per triangle, the brightest corner is
        // the tint (Valheim's own ambient occlusion shades the rest); 4 bits per channel keeps the
        // material count small, and near-white is white.
        public static uint Quantise(Color32 c)
        {
            if (c.r >= 236 && c.g >= 236 && c.b >= 236) return 0xFFFFFFu | (uint)Q(c.a) << 24;
            return (uint)Q(c.r) | (uint)Q(c.g) << 8 | (uint)Q(c.b) << 16 | (uint)Q(c.a) << 24;
        }

        static byte Q(byte v) => v >= 248 ? (byte)255 : (byte)((v & 0xF0) | 0x08);
    }
}
