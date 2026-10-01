using System;
using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // Minecraft's own frame (first-person hand, HUD, screens; transparent where the world would be),
    // read back by the Fabric mod and published through the overlay triple buffer, drawn over
    // Valheim's picture. Rows arrive bottom-up, which is how Unity textures are stored already.
    public static class Overlay
    {
        static Texture2D _tex;
        static bool _haveFrame;
        static Material _material;
        static bool _triedMaterial;
        public static long Frames;

        public static unsafe void Upload()
        {
            if (!Shm.Valid || !Shm.AcquireOverlayFrame()) return;
            var hdr = Shm.FrontHeader;
            int w = (int)hdr->width, h = (int)hdr->height;
            if (w <= 0 || h <= 0 || w > Proto.MaxOverlayW || h > Proto.MaxOverlayH) return;
            if (_tex == null || _tex.width != w || _tex.height != h)
            {
                if (_tex) UnityEngine.Object.Destroy(_tex);
                // linear: true. Minecraft's bytes are already display colours; as an sRGB texture they were
                // linearised on sampling but never converted back by the GUI pass, which darkened the HUD.
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "ValCraft overlay" };
                Plugin.Log($"overlay texture {w}x{h}");
            }
            _tex.LoadRawTextureData((IntPtr)Shm.FrontPixels, w * h * 4);
            _tex.Apply(false, false);
            _haveFrame = true;
            Frames++;
        }

        public static void Draw()
        {
            if (!_haveFrame || !_tex || Event.current.type != EventType.Repaint) return;
            if (!Puppet.MinecraftOwnsPlayer || Puppet.ValheimMenuOpen && !Puppet.McScreenOpen) return;
            // Minecraft's frame is premultiplied; GUI.DrawTexture blends straight alpha, which is
            // exact for opaque pixels and Minecraft's black screen dimming, slightly dark elsewhere.
            if (!_triedMaterial && Player.m_localPlayer) { _triedMaterial = true; _material = PremultipliedMaterial(); }
            var rect = new Rect(0, 0, Screen.width, Screen.height);
            if (_material != null) Graphics.DrawTexture(rect, _tex, new Rect(0, 0, 1, 1), 0, 0, 0, 0, Color.white, _material);
            else GUI.DrawTexture(rect, _tex, ScaleMode.StretchToFill, true);
        }

        // Some Unity built-in shader that blends One, OneMinusSrcAlpha, if the game ships one.
        static Material PremultipliedMaterial()
        {
            foreach (var name in new[] { "Legacy Shaders/Particles/Alpha Blended Premultiply", "Mobile/Particles/Alpha Blended Premultiply" })
            {
                var s = WorldRender.FindShader(name);
                if (s)
                {
                    Plugin.Log("overlay shader: " + name);
                    return new Material(s);
                }
            }
            Plugin.Log("overlay shader: none premultiplied; using GUI.DrawTexture");
            return null;
        }
    }
}
