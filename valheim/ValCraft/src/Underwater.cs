using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ValCraft.Link;
using ValCraft.Render;

namespace ValCraft
{
    // In vanilla Valheim the camera never goes under water, so there's no underwater look; diving
    // as Minecraft looked like being on land. While the camera is below Valheim's water (waves
    // included) or in a Minecraft water / lava block:
    //  - dense fog in the water's colour, darker the deeper;
    //  - the world tinted (inside the camera's render, so no HUD is);
    //  - the surface seen from below (Valheim's water is one-sided): a wavy translucent sheet;
    //  - world sounds muffled (music isn't).
    public static class Underwater
    {
        public enum Medium { None, Water, Lava }
        public static Medium Current { get; private set; }

        static float _depth;
        static bool _hooked, _applied;
        static bool _savedFog;
        static Color _savedFogColor, _savedSunFog;
        static float _savedFogDensity;
        static readonly int SunFogColor = Shader.PropertyToID("_SunFogColor");

        // Every frame, after Puppet (the camera is where Minecraft's eye is).
        public static void Frame()
        {
            Current = Medium.None;
            var cam = Camera.main;
            if (cam && Puppet.Puppeting && Puppet.MinecraftOwnsPlayer)
            {
                uint flags = Puppet.Mc.flags;
                Vector3 eye = cam.transform.position;
                float level = Floating.GetLiquidLevel(eye, 1f, LiquidType.Water);
                if ((flags & Proto.McEyeInLava) != 0) Current = Medium.Lava;
                else if (level > -5000f && eye.y < level) { Current = Medium.Water; _depth = level - eye.y; }
                else if ((flags & Proto.McEyeInWater) != 0) { Current = Medium.Water; _depth = 1f; }
            }
            if (!_hooked)
            {
                _hooked = true;
                Camera.onPreCull += OnPreCull;
                Camera.onPostRender += OnPostRender;
            }
            Muffle(Current != Medium.None);
            Tint(cam);
            Surface.Frame(cam, Current == Medium.Water && _depth > 0.05f && _depth < 60f);
        }

        // Fog only around the main camera's render: Valheim works its fog out again from scratch
        // when it next updates, and nothing else sees ours.
        static void OnPreCull(Camera cam)
        {
            if (Current == Medium.None || cam != Camera.main || _applied) return;
            _savedFog = RenderSettings.fog;
            _savedFogColor = RenderSettings.fogColor;
            _savedFogDensity = RenderSettings.fogDensity;
            _savedSunFog = Shader.GetGlobalColor(SunFogColor);
            _applied = true;
            var colour = FogColour(out float density);
            RenderSettings.fog = true;
            RenderSettings.fogColor = colour;
            RenderSettings.fogDensity = density;
            Shader.SetGlobalColor(SunFogColor, colour);
        }

        static void OnPostRender(Camera cam)
        {
            if (!_applied || cam != Camera.main) return;
            _applied = false;
            RenderSettings.fog = _savedFog;
            RenderSettings.fogColor = _savedFogColor;
            RenderSettings.fogDensity = _savedFogDensity;
            Shader.SetGlobalColor(SunFogColor, _savedSunFog);
        }

        // Lit like the world above (day, night, storms), fading towards black with depth.
        static float Light => Mathf.Clamp(RenderSettings.ambientLight.grayscale * 2.5f, 0.12f, 1f) * Mathf.Exp(-_depth / 30f);

        static Color FogColour(out float density)
        {
            if (Current == Medium.Lava) { density = 0.8f; return new Color(0.6f, 0.1f, 0f); }
            density = 0.03f;
            return new Color(0.05f, 0.2f, 0.28f) * Light;
        }

        // ---- the tint: a full-screen quad at the end of the camera's own render ----------------

        static CommandBuffer _tintCmd;
        static Camera _tintCam;
        static Material _tintMat;

        static void Tint(Camera cam)
        {
            bool on = Current != Medium.None && cam;
            if (_tintCam && (!on || _tintCam != cam)) { _tintCam.RemoveCommandBuffer(CameraEvent.AfterImageEffects, _tintCmd); _tintCam = null; }
            if (!on) return;
            if (_tintMat == null)
            {
                var shader = Shader.Find("UI/Default");
                if (!shader) return;
                _tintMat = new Material(shader) { name = "ValCraft underwater tint" };
                _tintCmd = new CommandBuffer { name = "ValCraft underwater tint" };
                _tintCmd.Blit(Texture2D.whiteTexture, BuiltinRenderTextureType.CameraTarget, _tintMat);
            }
            _tintMat.color = Current == Medium.Lava ? new Color(0.9f, 0.3f, 0f, 0.55f) : new Color(0.05f, 0.25f, 0.4f, 0.25f);
            if (!_tintCam) { cam.AddCommandBuffer(CameraEvent.AfterImageEffects, _tintCmd); _tintCam = cam; }
        }

        // ---- sound ---------------------------------------------------------------------------

        // Valheim mixes everything through its audio mixer, so a filter on the listener muffles the
        // music too. Instead each world sound (the SFX and Ambient mixer groups) gets its own
        // low-pass filter while under; new sounds are picked up a few times a second.
        static readonly List<AudioLowPassFilter> _filters = new List<AudioLowPassFilter>();
        static float _scanTimer;

        static void Muffle(bool on)
        {
            if (!on)
            {
                if (_filters.Count == 0) return;
                foreach (var f in _filters) if (f) f.enabled = false;
                _filters.Clear();
                _scanTimer = 0f;
                return;
            }
            _scanTimer -= Time.unscaledDeltaTime;
            if (_scanTimer > 0f) return;
            _scanTimer = 0.3f;
            foreach (var source in Object.FindObjectsOfType<AudioSource>())
            {
                if (!source || !source.outputAudioMixerGroup) continue;
                string group = source.outputAudioMixerGroup.name;
                if (group != "SFX" && group != "Ambient") continue;
                var f = source.GetComponent<AudioLowPassFilter>();
                if (!f) f = source.gameObject.AddComponent<AudioLowPassFilter>();
                else if (f.enabled && !_filters.Contains(f)) continue;  // someone else's filter, already on
                if (f.enabled && _filters.Contains(f)) continue;
                f.cutoffFrequency = 900f;
                f.enabled = true;
                _filters.Add(f);
            }
        }

        // ---- the surface from below ------------------------------------------------------------

        static class Surface
        {
            // Along each axis: fine 3 m steps out to 42 m, where the waves are seen up close, then
            // steps growing by 1.6x out to about 380 m, deep in the fog, so its edge never shows.
            const float Spacing = 3f;
            const int InnerSteps = 14, OuterSteps = 8;
            static readonly float[] Axis = BuildAxis();
            static GameObject _go;
            static Mesh _mesh;
            static MeshRenderer _renderer;
            static Vector3[] _verts;

            static float[] BuildAxis()
            {
                var outer = new List<float>();
                float d = InnerSteps * Spacing, step = Spacing;
                for (int i = 0; i < OuterSteps; i++) { step *= 1.6f; d += step; outer.Add(d); }
                var axis = new List<float>();
                for (int i = outer.Count - 1; i >= 0; i--) axis.Add(-outer[i]);
                for (int i = -InnerSteps; i <= InnerSteps; i++) axis.Add(i * Spacing);
                axis.AddRange(outer);
                return axis.ToArray();
            }

            public static void Frame(Camera cam, bool on)
            {
                if (!on || !cam || !BlockMaterials.Ready)
                {
                    if (_renderer) _renderer.enabled = false;
                    return;
                }
                if (!_go) Create();
                // Centred on the camera, snapped to the fine grid so the waves don't swim with it.
                Vector3 eye = cam.transform.position;
                float cx = Mathf.Round(eye.x / Spacing) * Spacing;
                float cz = Mathf.Round(eye.z / Spacing) * Spacing;
                float fallback = ZoneSystem.instance ? ZoneSystem.instance.m_waterLevel : 30f;
                int n = Axis.Length;
                for (int z = 0; z < n; z++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        var p = new Vector3(cx + Axis[x], eye.y, cz + Axis[z]);
                        float h = Floating.GetLiquidLevel(p, 1f, LiquidType.Water);
                        _verts[z * n + x] = new Vector3(p.x, h > -5000f ? h : fallback, p.z);
                    }
                }
                _mesh.vertices = _verts;
                _mesh.RecalculateBounds();
                _renderer.sharedMaterial = BlockMaterials.Get(BlockMaterials.Layer.Translucent, SurfaceColour(), Texture2D.whiteTexture, glow: true);
                _renderer.enabled = true;
            }

            // Light blue, brighter by day; quantised like the block materials (few materials).
            static uint SurfaceColour()
            {
                float l = Mathf.Clamp(RenderSettings.ambientLight.grayscale * 2.5f, 0.2f, 1f);
                var c = new Color32((byte)(45 * l), (byte)(105 * l), (byte)(135 * l), 150);
                return BlockMaterials.Quantise(c);
            }

            static void Create()
            {
                _go = new GameObject("ValCraft water underside");
                Object.DontDestroyOnLoad(_go);
                int n = Axis.Length, cells = n - 1;
                _verts = new Vector3[n * n];
                // Both windings: seen from below whichever way the waves tilt it.
                var tris = new List<int>(cells * cells * 12);
                for (int z = 0; z < cells; z++)
                {
                    for (int x = 0; x < cells; x++)
                    {
                        int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                        tris.AddRange(new[] { a, b, d, a, d, c });
                        tris.AddRange(new[] { a, d, b, a, c, d });
                    }
                }
                _mesh = new Mesh { name = "water underside" };
                _mesh.MarkDynamic();
                _mesh.vertices = _verts;
                // Facing down, at the camera below (computed, the two windings' normals cancel out).
                var normals = new Vector3[n * n];
                for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.down;
                _mesh.normals = normals;
                _mesh.SetTriangles(tris, 0);
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = _go.AddComponent<MeshRenderer>();
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            }
        }
    }
}
